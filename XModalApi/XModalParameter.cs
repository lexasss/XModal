namespace XModalApi;

public class XModalParameter(
    string name,
    string? label = null,
    double displayMin = 0,
    double displayMax = 1,
    string unit = "")
{
    public string Name { get; } = name;
    public string Label { get; } = label ?? name;
    public double DisplayMin { get; } = displayMin;
    public double DisplayMax { get; } = displayMax;
    public string Unit { get; } = unit;

    public double Value { get; private set; } = 0;

    public event EventHandler<double>? ValueChanged;

    public void Set(double v)
    {
        Value = Math.Max(DisplayMin, Math.Min(DisplayMax, v));
        ValueChanged?.Invoke(this, Value);
    }

    public double SetSilently(double v)
    {
        Value = Math.Max(DisplayMin, Math.Min(DisplayMax, v));
        return Value;
    }

    public object ToDict() => new
    {
        name = Name,
        label = Label,
        display_min = DisplayMin,
        display_max = DisplayMax,
        unit = Unit
    };
}