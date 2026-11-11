using System.Text.Json;

namespace XModalApi;

public class XModalInput(
    string label,
    string deviceType,
    string version,
    string bridgeIp = "127.0.0.1")
    : XModalLive(label, deviceType, version, bridgeIp)
{
    // Publish channel value
    public async Task PublishAsync(string name, double value)
    {
        if (!_params.ContainsKey(name)
            || !IsConnected
            || OscPrefix == null)
            return;

        await SendAsync(new
        {
            type = "osc_broadcast",
            address = $"{OscPrefix}/{name}",
            args = new[] { value }
        });
    }

    #region Abstract

    protected override string AnnounceType => "input";
    protected override string AnnounceExtraName => "channels";

    protected override void HandleMessage(string type, JsonElement data)
    {
        // do nothing: this is input device, we don't expect to receive messages from the bridge
    }

    #endregion
}
