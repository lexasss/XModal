using XModalApi;

namespace XModalApiUsageExample;

/// <summary>
/// An example of creating a data source for XModalClient
/// </summary>
internal class SineSource : ISource, IDisposable
{
    public string Type { get; }
    public string Channel => "sine";
    public string Address { get; set; } = "";

    public event EventHandler<float[]>? ValueChanged;

    public SineSource(string type)
    {
        Type = type;
        _timer = new Timer(OnTimerElapsed, null, 0, 100);
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    #region Internal

    private readonly Timer _timer;
    private double _phase = 0.0;

    private void OnTimerElapsed(object? state)
    {
        _phase += 0.1;
        var value = 0.5 + 0.5 * Math.Sin(_phase);
        ValueChanged?.Invoke(this, [(float)value]);
    }

    #endregion
}