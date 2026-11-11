using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XModalApi;

/// <summary>
/// Sends and received data to/from XModal bridge
/// </summary>
public class XModalClient : IDisposable
{
    public enum SubscriberType
    {
        /// <summary>
        /// Input is the data that flows into the XModal bridge.
        /// You don't need to subscribe to input data unless you want to monitor it.
        /// Rather, you usually subscribe to the output data only.
        /// </summary>
        Input,

        /// <summary>
        /// Output is the data that is sent to the XModal clients/outputs
        /// from the XModal bridge when there is a corresponding node 
        /// in the XModal designer
        /// </summary>
        Output
    }

    public delegate void SubscriberDelegate(double[] values);

    public string Label { get; }
    public string Version { get; }
    public string DeviceType { get; }
    public string BridgeUrl { get; }

    public event EventHandler<int>? Connected;
    public event EventHandler? Disconnected;

    public XModalClient(
        string label = "X-Modal Client",
        string deviceType = "xmodal_client",
        string version = "0.1",
        string bridgeIp = "127.0.0.1")
    {
        Label = label;
        Version = version;
        DeviceType = ReplaceNonLetters(deviceType);
        BridgeUrl = $"ws://{bridgeIp}:8080";

        _xmodalTarget = new(bridgeIp, XMODAL_TARGET_PORT);
        _sessionId = $"browser_{DeviceType}_4456447"; // Guid.CreateVersion7().ToString()[^12..];

        _cts = new CancellationTokenSource();

        _ws = new ClientWebSocket();
        _ws
            .ConnectAsync(new Uri(BridgeUrl), _cts.Token)
            .ContinueWith(task => Run(), TaskContinuationOptions.NotOnFaulted)
            .ContinueWith(task => Announce(), TaskContinuationOptions.NotOnCanceled)
            .ContinueWith(task =>
            {
                if (_ws.State != WebSocketState.Open)
                {
                    Console.WriteLine($"Failed to connect to X-Modal bridge");
                }
            });
    }

    /// <summary>
    /// Subscribes for a data to receive it from XModal bridge
    /// </summary>
    /// <param name="category">top-level category</param>
    /// <param name="subcategory">second-level caetgory (subcategory)</param>
    /// <param name="channel">third level category (channel name)</param>
    /// <param name="subscriber">function to handle received data</param>
    /// <param name="type">category type, input or output. Default is 'output'</param>
    public void Subscribe(
        string category, 
        string subcategory, 
        string channel, 
        SubscriberDelegate subscriber,
        SubscriberType type = SubscriberType.Output)
    {
        var key = $"/cms/{type}/{category}/{subcategory}/*/{channel}".ToLower();
        var subscribers = type == SubscriberType.Input ? _inputSubscribers : _outputSubscribers;

        if (!subscribers.ContainsKey(key))
        {
            subscribers[key] = subscriber;
        }
        else
        {
            subscribers[key] += subscriber;
        }
    }

    /// <summary>
    /// Attaches data source.
    /// declares that a data source is activated
    /// </summary>
    /// <param name="source">data source</param>
    public void AttachSource(ISource source)
    {
        var key = $"/cms/input/{DeviceType}/{source.Type}/{_instance}/{source.Channel}";
        _sources.Add(key, source);
        source.Address = key;
        source.ValueChanged += ValueChanged;
    }

    public void Dispose()
    {
        _tickTimer?.Dispose();
        _ws.Dispose();
    }

    #region Internal

    record Target(string Ip, int Port);

    const int XMODAL_TARGET_PORT = 9001;

    readonly Dictionary<string, SubscriberDelegate> _inputSubscribers = [];
    readonly Dictionary<string, SubscriberDelegate> _outputSubscribers = [];
    readonly Dictionary<string, ISource> _sources = [];
    readonly SemaphoreSlim _sendLock = new(1, 1);
    readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = null
    };

    readonly string _sessionId;
    readonly Target _xmodalTarget;

    string? _instance = null;
    Timer? _tickTimer = null;
    readonly ClientWebSocket _ws;
    readonly CancellationTokenSource _cts;

    private async Task Run()
    {
        try
        {
            while (!_cts.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                var buffer = new byte[10240];
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    break;
                }

                if (!result.EndOfMessage)
                {
                    continue;
                }

                var json = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);

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
            Console.WriteLine($"Error in Run: {ex.Message}");
        }
        finally
        {
            _cts.Cancel();
            _ws.Dispose();
        }

        _instance = null;

        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private async void ValueChanged(object? sender, float[] values)
    {
        string address = ((ISource)sender!).Address;
        await SendValue(address, values);
    }

    private void Handle(JsonElement data)
    {
        string? type = data.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString()
            : null;

        if (type == "cms_assigned")
        {
            if (data.TryGetProperty("args", out var args) &&
                args.ValueKind == JsonValueKind.Array &&
                args.GetArrayLength() > 0)
            {
                HandleAssignedMessage(args[0].GetInt32());
            }
        }
        else if (type == "osc_input" ||
                 type == "osc_output")
        {
            if (data.TryGetProperty("address", out var addr) && 
                data.TryGetProperty("args", out var args) &&
                args.ValueKind == JsonValueKind.Array &&
                args.GetArrayLength() > 0)
            {
                List<double> values = [];
                foreach (var arg in args.EnumerateArray())
                {
                    if (arg.ValueKind == JsonValueKind.Number && arg.TryGetDouble(out double d))
                    {
                        values.Add(d);
                    }
                }

                var subscribers = type == "osc_input" ? _inputSubscribers : _outputSubscribers;
                HandleOscMessage(subscribers, addr.GetString() ?? "", values.ToArray());
            }
        }
        else
        {
            // Just ignore other messages
        }
    }

    private async Task Announce()
    {
        await SendAsync(new
        {
            address = "/cms/announce",
            args = (object[])[DeviceType, Version, _sessionId, 0],
            target = _xmodalTarget
        });
    }

    private async Task SendStatus()
    {
        if (_instance == null) 
            return;

        await SendAsync(new
        {
            address = "/cms/status",
            args = (object[])[DeviceType, _instance, "online", _sessionId, 0, Version],
            target = _xmodalTarget
        });
    }

    private async Task SendValue(string source, string channel, params float[] values)
    {
        await SendValue($"/cms/input/{DeviceType}/{source}/{_instance}/{channel}", values);
    }

    private async Task SendValue(string address, float[] values)
    {
        if (_instance == null || 
            values.Length == 0)
            return;

        await SendAsync(new
        {
            address,
            args = (float[])[..values],
            target = _xmodalTarget
        });
    }

    private async Task SendAsync(object obj)
    {
        if (_ws?.State != WebSocketState.Open)
            return;

        string json = JsonSerializer.Serialize(obj, _jsonSerializerOptions);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

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

    private void HandleAssignedMessage(int id)
    {
        _instance = id.ToString();
        _tickTimer = new Timer(async (state) =>
        {
            await SendStatus();
        }, null, 0, 5000);

        Connected?.Invoke(this, id);
    }

    private static void HandleOscMessage(
        Dictionary<string, SubscriberDelegate> subscribers,
        string address,
        double[] values)
    {
        var msgParts = address.Split('/');
        foreach (var subscriber in subscribers)
        {
            var subscriberParts = subscriber.Key.Split('/');
            if (msgParts.Length == subscriberParts.Length)
            {
                bool matches = true;
                for (int i = 0; i < msgParts.Length; i++)
                {
                    if (subscriberParts[i] != msgParts[i] && 
                        subscriberParts[i] != "*")
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    subscriber.Value(values);
                }
            }
        }
    }

    private static string ReplaceNonLetters(string input, bool collapse = true)
    {
        return Regex.Replace(input, collapse ? @"[^\p{L}]+" : @"[^\p{L}]", "_");
    }
    
    #endregion
}
