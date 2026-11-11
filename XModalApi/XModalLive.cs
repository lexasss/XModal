using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace XModalApi;

public abstract class XModalLive(
    string label = "X-Modal Comm",
    string deviceType = "xmodal_comm",
    string version = "0.1",
    string bridgeIp = "127.0.0.1")
{
    public string Label { get; } = label;
    public string Version { get; } = version;
    public string DeviceType { get; } = deviceType;
    public string BridgeUrl { get; } = $"ws://{bridgeIp}:8080";

    public string? OscPrefix => _oscPrefix;
    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public event EventHandler? OnReady;
    public event EventHandler? OnDisconnect;
    public event EventHandler? OnServerDisconnect;

    // Register a parameter
    public XModalParameter AddChannel(
        string name,
        string? label = null,
        double displayMin = 0,
        double displayMax = 1,
        string unit = "")
    {
        var p = new XModalParameter(name, label, displayMin, displayMax, unit);

        _params[name] = p;
        return p;
    }

    public void AddChannel(XModalParameter param)
    {
        _params[param.Name] = param;
    }

    // Connect to bridge
    public async Task ConnectAsync()
    {
        if (_ws != null)
            await DisconnectAsync();

        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();

        await _ws.ConnectAsync(new Uri(BridgeUrl), _cts.Token);

        // Start receiving messages in the background
        _ = ReceiveLoopAsync(_ws, _cts.Token);

        await AnnounceAsync();
    }

    // Disconnect from bridge
    public async Task DisconnectAsync()
    {
        if (_ws == null)
            return;

        if (_deviceId != null)
        {
            await SendAsync(new
            {
                type = $"{AnnounceType}_retract",
                device_id = _deviceId
            });
        }

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

        _cts?.Cancel();

        _ws.Dispose();
        _ws = null;
        _instance = null;
    }

    #region Abstract / Virtual

    protected abstract string AnnounceType { get; }
    protected abstract string AnnounceExtraName { get; }
    protected virtual XModalParameter[] AnnounceExtraValues => _params.Values.ToArray();

    protected abstract void HandleMessage(string type, JsonElement data);

    #endregion

    #region Internal

    protected readonly Dictionary<string, XModalParameter> _params = [];
    readonly SemaphoreSlim _sendLock = new(1, 1);
    readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    ClientWebSocket? _ws;
    string? _deviceId;
    protected int? _instance;
    string? _oscPrefix;

    CancellationTokenSource? _cts;
    bool _serverDisconnected;

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
                    Handle(doc.RootElement);
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
            Console.WriteLine("Error: " + ex);
        }
        finally
        {
            _instance = null;

            if (_serverDisconnected)
            {
                _serverDisconnected = false;

                OnServerDisconnect?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                OnDisconnect?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void Handle(JsonElement data)
    {
        string? type = data.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString()
            : null;

        if (type == $"{AnnounceType}_assigned")
        {
            _deviceId = data.GetProperty("device_id").GetString();
            _instance = data.GetProperty("instance").GetInt32();
            _oscPrefix = data.GetProperty("osc_prefix").GetString();

            Console.WriteLine($"Assigned as {_deviceId} with prefix {_oscPrefix}");

            OnReady?.Invoke(this, EventArgs.Empty);
        }
        else if (type == "server_disconnect")
        {
            _serverDisconnected = true;
            return;
        }
        else if (type != null)
        {
            // Handle other message types in derived classes
            HandleMessage(type, data);
        }
    }

    private async Task AnnounceAsync()
    {
        var announce = new Dictionary<string, object>
        {
            ["type"] = $"{AnnounceType}_announce",
            ["device_type"] = DeviceType,
            ["label"] = Label,
            ["version"] = Version,
            [AnnounceExtraName] = AnnounceExtraValues.Select(p => p.ToDict()).ToArray()
        };

        await SendAsync(announce);
    }

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
        finally
        {
            _sendLock.Release();
        }
    }

    #endregion
}
