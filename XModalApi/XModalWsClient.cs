using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XModalApi;

public abstract class XModalWsClient(
    string label,
    string deviceType,
    string version,
    string bridgeIp = "127.0.0.1")
{
    public string Label { get; } = label;
    public string Version { get; } = version;
    public string DeviceType { get; } = ReplaceNonLetters(deviceType);
    public string BridgeUrl { get; } = $"ws://{bridgeIp}:8080";

    public bool IsConnected => _ws?.State == WebSocketState.Open;
    public string? DeviceId => _deviceId;

    public event EventHandler<int>? Ready;
    public event EventHandler<DisconnectionReason>? Disconnected;
    public event EventHandler<string>? Error;

    /// <summary>
    /// Connect to bridge
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_ws != null)
            await DisconnectAsync();

        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();

        try
        {
            await _ws.ConnectAsync(new Uri(BridgeUrl), _cts.Token);

            // Start receiving messages in the background
            _ = ReceiveLoopAsync(_ws, _cts.Token);

            await AnnounceAsync();
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex.Message);
        }
    }

    public async Task DisconnectAsync()
    {
        if (_ws == null)
            return;

        await DeannounceAsync();

        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                await _ws.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client disconnect",
                    CancellationToken.None);
            }
        }
        catch
        {
            // Ignore connection errors during shutdown.
        }

        CleanUp();

        _cts?.Cancel();

        _ws.Dispose();
        _ws = null;
    }

    #region Shared with derived classes
    
    protected CancellationTokenSource? _cts;
    protected ClientWebSocket? _ws;
    protected int? _instance;
    protected string? _deviceId;

    protected bool _serverDisconnected = false; // to be set in derived classes when the server sends disconnection message.

    protected void OnReady(int code) => Ready?.Invoke(this, code);

    protected abstract Task AnnounceAsync();
    protected abstract Task DeannounceAsync();
    protected abstract void HandleMessage(JsonElement data);
    protected abstract void CleanUp();

    protected async Task SendAsync(object obj)
    {
        if (_ws?.State != WebSocketState.Open)
            return;

        string json = JsonSerializer.Serialize(obj, _jsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync();

        try
        {
            if (_ws?.State == WebSocketState.Open)
            {
                await _ws.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // Falls here when the bridge goes down
        }
        finally
        {
            _sendLock.Release();
        }
    }

    protected static string ReplaceNonLetters(string input, bool collapse = true)
    {
        return Regex.Replace(input, collapse ? @"[^\p{L}]+" : @"[^\p{L}]", "_");
    }

    #endregion

    #region Internal

    readonly SemaphoreSlim _sendLock = new(1, 1);
    readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    private async Task ReceiveLoopAsync(
        ClientWebSocket ws,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        try
        {
            while (ws.State == WebSocketState.Open &&
                   !cancellationToken.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                try
                {
                    using var doc = JsonDocument.Parse(json);
                    HandleMessage(doc.RootElement);
                }
                catch (JsonException)
                {
                    // Ignore invalid JSON.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during disconnect.
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex.Message);
        }
        finally
        {
            _instance = null;

            Disconnected?.Invoke(this, _serverDisconnected
                ? DisconnectionReason.StoppedFromServer
                : DisconnectionReason.ConnectionLost);

            _serverDisconnected = false;
        }
    }

    #endregion
}
