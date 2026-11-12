using System.Text.Json;

namespace XModalApi;

/// <summary>
/// Sends and received data to/from XModal bridge
/// </summary>
public class StaticClient(
    string label,
    string deviceType,
    string version,
    int session = 1,
    string bridgeIp = "127.0.0.1")
    : XModalWsClient(label, deviceType, version, bridgeIp)
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

    /// <summary>
    /// Subscribes for a data to receive it from XModal bridge
    /// </summary>
    /// <param name="category">top-level category</param>
    /// <param name="subcategory">second-level category (subcategory)</param>
    /// <param name="channel">third level category (channel name)</param>
    /// <param name="subscriber">function to handle received data</param>
    /// <param name="type">source type, input or output. Default is 'output'</param>
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
    /// Attaches data source,
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

    #region Overrides

    protected override async Task AnnounceAsync()
    {
        await SendAsync(new
        {
            address = "/cms/announce",
            args = (object[])[DeviceType, Version, _sessionId, 0],
            target = _xmodalTarget
        });
    }

    protected override async Task DeannounceAsync()
    {
        // do nothing, we don't need to retract anything on disconnect
    }

    protected override void HandleMessage(JsonElement data)
    {
        string? type = data.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString()
            : null;

        if (type == "cms_assigned")
        {
            var id = data.GetProperty("args")[0].GetInt32();
            _deviceId = data.GetProperty("device_id").GetString();

            HandleAssignedMessage(id);
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
        else if (type == "server_disconnect")
        {
            _serverDisconnected = true;
        }
        else
        {
            // Just ignore other messages
        }
    }

    protected override void CleanUp()
    {
        _tickTimer?.Dispose();
        _sources.Clear();
    }

    #endregion

    #region Internal

    record Target(string Ip, int Port = 9001);

    readonly Dictionary<string, SubscriberDelegate> _inputSubscribers = [];
    readonly Dictionary<string, SubscriberDelegate> _outputSubscribers = [];
    readonly Dictionary<string, ISource> _sources = [];

    readonly string _sessionId = $"browser_{ReplaceNonLetters(deviceType)}_{session}"; // Guid.CreateVersion7().ToString()[^12..];
    readonly Target _xmodalTarget = new(bridgeIp);

    Timer? _tickTimer = null;

    private async void ValueChanged(object? sender, float[] values)
    {
        string address = ((ISource)sender!).Address;
        await SendValuesAsync(address, values);
    }

    private async Task SendStatusAsync()
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

    private async Task SendValuesAsync(string source, string channel, params float[] values)
    {
        await SendValuesAsync($"/cms/input/{DeviceType}/{source}/{_instance}/{channel}", values);
    }

    private async Task SendValuesAsync(string address, float[] values)
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

    private void HandleAssignedMessage(int id)
    {
        _instance = id;
        _tickTimer = new Timer(async (state) =>
        {
            await SendStatusAsync();
        }, null, 0, 5000);

        OnReady(id);
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
    
    #endregion
}
