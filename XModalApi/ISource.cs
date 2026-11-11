namespace XModalApi;

public interface ISource
{
    string Type { get; }
    string Channel { get; }
    string Address { get; set; }
    event EventHandler<float[]>? ValueChanged;
}
