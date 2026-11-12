using XModalApi;

namespace XModalApiUsageExample;

internal class Program
{
    static async Task Main()
    {
        Console.WriteLine("XModal tasks: ");
        Console.WriteLine(" 1. Test dynamic output (read + feedback)");
        Console.WriteLine(" 2. Test dynamic input (write)");
        Console.WriteLine(" 3. Test static client (read + write)");
        Console.Write("Choose the task: ");

        var input = Console.ReadLine();
        if (!int.TryParse(input, out int task))
        {
            return;
        }

        Console.WriteLine();

        if (task == 1)
            await TestXModalOutput();
        else if (task == 2)
            await TestXModalInput();
        else if (task == 3)
            await TestXModalClient();
        else
            Console.WriteLine("Unknown task. Exiting...");
    }

    static async Task TestXModalOutput()
    {
        Console.WriteLine(" == Receives data from output nodes ==");
        Console.WriteLine(" == Sets data for live feedback nodes ==\n");
        Console.WriteLine(" Go to XModal designed and wire up something");
        Console.WriteLine(" to the 'X-Modal Output - TUNI Connect - get' output node.");
        Console.WriteLine(" The 'X-Modal Output - TUNI Connect - set' output node");
        Console.WriteLine(" will be controlled from this app, thus providing live feedback.\n");
        Console.WriteLine(" Note that the output node category is not visible");
        Console.WriteLine(" in the 'Filter by category' field.");
        Console.WriteLine(" Type 'tuni' in the search box instead, and scroll to the bottom.\n");

        var xlo = new LiveOutput("TUNI Connect", "tuni_output", "1.0");

        var paramToSend = xlo.AddChannel("set");
        Console.WriteLine($"  Created 'X-Modal Output - {xlo.Label} - set' output node");

        var paramToReceive = xlo.AddChannel("get");
        paramToReceive.ValueChanged += (s, e) => Console.WriteLine($"{xlo.OscPrefix}/{paramToReceive.Label} = {e:F2}");
        Console.WriteLine($"  Created 'X-Modal Output - {xlo.Label} - get' output node");

        Console.WriteLine();

        xlo.Ready += (s, e) => {
            Console.WriteLine($"{xlo.Label} got ID = {xlo.DeviceId}. OSC prefix = {xlo.OscPrefix}");
        };

        xlo.Error += (s, e) =>
        {
            Console.WriteLine($"Error: {e}");
        };

        xlo.Disconnected += (s, e) => {
            Console.WriteLine("Disconnected" + (e == DisconnectionReason.StoppedFromServer ? " by server" : ""));
        };

        await xlo.ConnectAsync();

        // Emulate data production
        _ = Task.Run(async () =>
        {
            double t = 0;
            var speed = 2.5;
            while (xlo.IsConnected)
            {
                var value = (Math.Sin(t * speed) + 1) / 2;
                await xlo.PublishAsync("set", value);

                t += 0.02 * speed;

                await Task.Delay(100);
            }
        });

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey(intercept: true);

        await xlo.DisconnectAsync();
    }

    static async Task TestXModalInput()
    {
        Console.WriteLine(" == Sends 'a' and 'b' emulated data ==\n");
        Console.WriteLine(" Note that the input node category is not visible");
        Console.WriteLine(" in the 'Filter by category' field.");
        Console.WriteLine(" Type 'tuni' in the search box instead, and scroll to the bottom.\n");

        var xli = new LiveInput("TUNI Input", "tuni_input", "1.0");

        LiveParameter[] channels = [
            new("a", label: "Alpha", unit: "µV"),
            new("b", label: "Beta", unit: "µV")
        ];

        foreach (var channel in channels)
        {
            xli.AddChannel(channel);
            Console.WriteLine($"  Created 'X-Modal Connect - {xli.Label} - {channel.Label}' input node");
        }

        Console.WriteLine();

        xli.Ready += (s, e) => {
            Console.WriteLine($"{xli.Label} got ID = {xli.DeviceId}. OSC prefix = {xli.OscPrefix}");
        };

        xli.Error += (s, e) =>
        {
            Console.WriteLine($"Error: {e}");
        };

        xli.Disconnected += (s, e) => {
            Console.WriteLine("Disconnected" + (e == DisconnectionReason.StoppedFromServer ? " by server" : ""));
        };

        await xli.ConnectAsync();

        // Emulate data production
        _ = Task.Run(async () =>
        {
            while (xli.IsConnected)
            {
                double a = 50 + 10 * Math.Sin(DateTime.Now.Ticks / 1e7);
                double b = 30 + 5 * Math.Cos(DateTime.Now.Ticks / 1e7);
                
                await xli.PublishAsync("a", a / 100);
                await xli.PublishAsync("b", b / 100);

                await Task.Delay(100);
            }
        });

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey(intercept: true);

        await xli.DisconnectAsync();
    }

    static async Task TestXModalClient()
    {
        Console.WriteLine(" == Activates sine and LeapMotion input nodes ==");
        Console.WriteLine(" == Receives data from `slot1` output node ==\n");
        Console.WriteLine(" Note that 'sine' and three Leap Motion input nodes");
        Console.WriteLine(" are defined in xmodal/designer/input_catalog.json");
        Console.WriteLine(" as /cms/input/tuni/periodic/sine and /cms/input/tuni/leap_motion/palm_{x,y,z};");
        Console.WriteLine(" the `slot1` output node is defined in xmodal/designer/output_catalog.json");
        Console.WriteLine(" as /cms/output/tuni/monitor/slot1\n");

        var lm = new LeapMotion();

        var xsc = new StaticClient("TUNI Client", "tuni", "1.0");

        /*
        // receives data directly from TestSignal/sine input
        Console.WriteLine("Run 'Test Signal' from XModal main page to receive test/test_data/sine input.");
        client.Subscribe("testing", "test_signal", "sine", value =>
        {
            Console.WriteLine($"/input/testing/test_signal/sine = {value[0]}");
        }, XModalClient.SubscriberType.Input);
        */

        /*
        // receives data directly from TuniSignal/stair input
        Console.WriteLine("Run 'TUNI Signal' from XModal main page to receive tuni/signal/stair input.");
        client.Subscribe("tuni", "signal", "stair", values =>
        {
            Console.WriteLine($"input/tuni/signal/stair = {values[0]}");
        }, XModalClient.SubscriberType.Input);
        */

        Console.WriteLine($" Create a 'TUNI - Monitor - slot1' ({xsc.DeviceType}/monitor/slot1) output node");
        Console.WriteLine(" in the XModal designer and wire it to some input node.\n");

        xsc.Subscribe(xsc.DeviceType, "monitor", "slot1", values =>
        {
            Console.WriteLine($"output/{xsc.DeviceType}/monitor/slot1 = {values[0]}");
        });

        xsc.Ready += (s, e) =>
        {
            Console.WriteLine($"{xsc.Label} got ID = {xsc.DeviceId}.");

            Console.WriteLine($" Activated 'TUNI - Sine' ({xsc.DeviceType}/periodic/sine) input node.");
            xsc.AttachSource(new SineSource("periodic"));

            foreach (var lmChannel in lm.Channels)
            {
                Console.WriteLine($" Activated 'TUNI - {ToHungarianStyle(lmChannel.Channel)}' ({xsc.DeviceType}/{lmChannel.Type}/{lmChannel.Channel}) input node.");
                xsc.AttachSource(lmChannel);
            }
            Console.WriteLine();

            lm.Run();
        };

        xsc.Error += (s, e) =>
        {
            Console.WriteLine($"Error: {e}");
        };

        xsc.Disconnected += (s, e) =>
        {
            Console.WriteLine("Disconnected" + (e == DisconnectionReason.StoppedFromServer ? " by server" : ""));
        };

        await xsc.ConnectAsync();

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey(intercept: true);

        await xsc.DisconnectAsync();
    }

    static string ToHungarianStyle(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return string.Concat(
            text.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
        );
    }
}
