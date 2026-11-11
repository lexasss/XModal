using Leap;
using XModalApi;

namespace XModalApiUsageExample;

internal class LeapMotionChannel(string name) : ISource
{
    public string Type => "leap_motion";
    public string Channel => name;
    public string Address { get; set; } = "";

    public event EventHandler<float[]>? ValueChanged;

    public void Publish(float value)
    {
        ValueChanged?.Invoke(this, [value]);
    }
}

internal class LeapMotion: IDisposable
{
    public LeapMotionChannel[] Channels => _channels;

    public event EventHandler<bool>? ServiceConnectionChanged;
    public event EventHandler<bool>? DeviceConnectionChanged;
    public event EventHandler<bool>? HandVisibilityChanged;

    public LeapMotion() 
    {
        _controller = new Controller();
        
        _controller.SetPolicy(Controller.PolicyFlag.POLICY_DEFAULT);

        _controller.Connect += OnServiceConnect;
        _controller.Disconnect += OnServiceDisconnect;
        _controller.Device += OnConnect;
        _controller.DeviceLost += OnDisconnect;
        _controller.DeviceFailure += OnDeviceFailure;
        _controller.LogMessage += OnLogMessage;
    }

    public void Run()
    {
        _controller?.FrameReady += OnFrame;
    }

    public void Dispose()
    {
        _controller?.Dispose();
    }

    #region Internal

    readonly Controller? _controller = null;
    readonly LeapMotionChannel[] _channels =
    [
        new LeapMotionChannel("palm_x"),
        new LeapMotionChannel("palm_y"),
        new LeapMotionChannel("palm_z"),
    ];

    bool _isHandVisible = false;

    private void OnConnect(object? sender, DeviceEventArgs args)
    {
        Console.WriteLine($"[LEAP] Connected to {args.Device.SerialNumber}");
        DeviceConnectionChanged?.Invoke(this, true);
    }

    private void OnDisconnect(object? sender, DeviceEventArgs args)
    {
        Console.WriteLine("[LEAP] Disconnected");
        DeviceConnectionChanged?.Invoke(this, false);
    }

    private void OnFrame(object? sender, FrameEventArgs args)
    {
        Frame frame = args.frame;

        if (frame.Hands.Count > 0)
        {
            if (!_isHandVisible)
            {
                _isHandVisible = true;
                HandVisibilityChanged?.Invoke(this, _isHandVisible);
            }

            var palmPos = frame.Hands[0].PalmPosition;

            _channels[0].Publish(palmPos.x);
            _channels[1].Publish(palmPos.y);
            _channels[2].Publish(palmPos.z);
        }
        else if (_isHandVisible)
        {
            _isHandVisible = false;
            HandVisibilityChanged?.Invoke(this, _isHandVisible);
        }
    }

    private void OnServiceConnect(object? sender, ConnectionEventArgs args)
    {
        Console.WriteLine("[LEAP] Service Connected");

        ServiceConnectionChanged?.Invoke(this, true);
    }

    private void OnServiceDisconnect(object? sender, ConnectionLostEventArgs args)
    {
        Console.WriteLine("[LEAP] Service Disconnected");

        ServiceConnectionChanged?.Invoke(this, false);
    }

    private void OnDeviceFailure(object? sender, DeviceFailureEventArgs args)
    {
        Console.WriteLine("[LEAP] Device Error");
        Console.WriteLine($"   PNP ID: {args.DeviceSerialNumber}");
        Console.WriteLine($"   Failure message: {args.ErrorMessage}"    );
    }

    private void OnLogMessage(object? sender, LogEventArgs args)
    {
        var (severity, level) = args.severity switch
        {
            MessageSeverity.MESSAGE_CRITICAL => ("Critical", "Critical"),
            MessageSeverity.MESSAGE_WARNING => ("Warning", "Warning"),
            MessageSeverity.MESSAGE_INFORMATION => ("Info", "Information"),
            _ => ("Unknown", "Debug"),
        };

        Console.WriteLine($"[{severity}] {args.type}: {args.message}");
    }

    #endregion
}
