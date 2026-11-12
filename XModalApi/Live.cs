using System.Text.Json;

namespace XModalApi;

public abstract class Live(
    string label,
    string deviceType,
    string version,
    string bridgeIp = "127.0.0.1")
    : XModalWsClient(label, deviceType, version, bridgeIp)
{
    public string? OscPrefix => _oscPrefix;

    // Register a parameter
    public LiveParameter AddChannel(
        string name,
        string? label = null,
        double displayMin = 0,
        double displayMax = 1,
        string unit = "")
    {
        var p = new LiveParameter(name, label, displayMin, displayMax, unit);

        _params[name] = p;
        return p;
    }

    public void AddChannel(LiveParameter param)
    {
        _params[param.Name] = param;
    }

    #region Abstract / Virtual

    protected abstract string AnnounceType { get; }
    protected abstract string AnnounceExtraName { get; }
    protected virtual LiveParameter[] AnnounceExtraValues => _params.Values.ToArray();

    protected abstract void HandleMessage(string type, JsonElement data);

    #endregion

    #region Internal

    protected readonly Dictionary<string, LiveParameter> _params = [];

    string? _oscPrefix;

    protected override async Task AnnounceAsync()
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

    protected override async Task DeannounceAsync()
    {
        if (_deviceId != null)
        {
            await SendAsync(new
            {
                type = $"{AnnounceType}_retract",
                device_id = _deviceId
            });
        }
    }

    protected override void HandleMessage(JsonElement data)
    {
        string? type = data.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString()
            : null;

        if (type == $"{AnnounceType}_assigned")
        {
            _deviceId = data.GetProperty("device_id").GetString();
            _instance = data.GetProperty("instance").GetInt32();
            _oscPrefix = data.GetProperty("osc_prefix").GetString();

            OnReady(_instance ?? 0);
        }
        else if (type == "server_disconnect")
        {
            _serverDisconnected = true;
        }
        else if (type != null)
        {
            // Handle other message types in derived classes
            HandleMessage(type, data);
        }
    }

    protected override void CleanUp()
    {
        _params.Clear();
        _deviceId = null;
        _oscPrefix = null;
    }

    #endregion
}
