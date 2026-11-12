using System.Text.Json;

namespace XModalApi;

public class LiveOutput(
    string label,
    string deviceType,
    string version,
    string bridgeIp = "127.0.0.1")
    : Live(label, deviceType, version, bridgeIp)
{
    // Set parameter value and send live feedback
    public async Task PublishAsync(string name, double value)
    {
        if (!_params.TryGetValue(name, out var p) ||
            !IsConnected ||
            _instance == null)
            return;

        double v = p.SetSilently(value);

        await SendAsync(new
        {
            type = "live_feedback",
            device_type = DeviceType,
            instance = _instance,
            param = name,
            label = p.Label,
            value = v,
            osc_address = $"{OscPrefix}/{name}",
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0
        });
    }

    #region Abstract

    protected override string AnnounceType => "module";
    protected override string AnnounceExtraName => "parameters";

    protected override void HandleMessage(string type, JsonElement data)
    {
        if (type == "osc_output")
        {
            string address = data.TryGetProperty("address", out var addr)
                ? addr.GetString() ?? ""
                : "";

            string[] parts = address.Split('/');

            string paramName = parts[^1];

            if (_params.TryGetValue(paramName, out var p) &&
                data.TryGetProperty("args", out var args) &&
                args.ValueKind == JsonValueKind.Array &&
                args.GetArrayLength() > 0)
            {
                var value = args[0];

                if (value.ValueKind == JsonValueKind.Number)
                    p.Set(value.GetDouble());
                else if (value.ValueKind == JsonValueKind.String &&
                         double.TryParse(value.GetString(), out double parsed))
                    p.Set(parsed);
            }
        }
    }

    #endregion
}
