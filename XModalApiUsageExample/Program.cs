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

        var xo = new XModalOutput("TUNI Connect", "tuni_output", "1.0");

        var paramToSend = xo.AddChannel("set");
        Console.WriteLine($"  Created 'X-Modal Output - {xo.Label} - set' output node");

        var paramToReceive = xo.AddChannel("get");
        paramToReceive.ValueChanged += (s, e) => Console.WriteLine($"{xo.OscPrefix}/{paramToReceive.Label} = {e:F2}");
        Console.WriteLine($"  Created 'X-Modal Output - {xo.Label} - get' output node");

        Console.WriteLine();

        xo.OnReady += (s, e) => {
            Console.WriteLine($"Connected: {xo.DeviceType}"); 
        };

        xo.OnDisconnect += (s, e) => {
            Console.WriteLine("Disconnected"); 
        };

        xo.OnServerDisconnect += (s, e) => {
            Console.WriteLine("Bridge disconnected");
        };

        await xo.ConnectAsync();

        _ = Task.Run(async () =>
        {
            double t = 0;
            var speed = 2.5;
            while (xo.IsConnected)
            {
                var value = (Math.Sin(t * speed) + 1) / 2;
                await xo.PublishAsync("set", value);

                t += 0.02 * speed;

                await Task.Delay(100);
            }
        });

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey(intercept: true);

        await xo.DisconnectAsync();
    }

    static async Task TestXModalInput()
    {
        Console.WriteLine(" == Sends 'a' and 'b' emulated data ==\n");
        Console.WriteLine(" Note that the input node category is not visible");
        Console.WriteLine(" in the 'Filter by category' field.");
        Console.WriteLine(" Type 'tuni' in the search box instead, and scroll to the bottom.\n");

        var xi = new XModalInput("TUNI Input", "tuni_input", "1.0");

        XModalParameter[] channels = [
            new("a", label: "Alpha", unit: "µV"),
            new("b", label: "Beta", unit: "µV")
        ];

        foreach (var channel in channels)
        {
            xi.AddChannel(channel);
            Console.WriteLine($"  Created 'X-Modal Connect - {xi.Label} - {channel.Label}' input node");
        }

        Console.WriteLine();

        xi.OnReady += (s, e) => {
            Console.WriteLine($"Connected: {xi.DeviceType}");
        };

        xi.OnDisconnect += (s, e) => {
            Console.WriteLine("Disconnected");
        };

        xi.OnServerDisconnect += (s, e) => {
            Console.WriteLine("Bridge disconnected");
        };

        await xi.ConnectAsync();

        _ = Task.Run(async () =>
        {
            while (xi.IsConnected)
            {
                double a = 50 + 10 * Math.Sin(DateTime.Now.Ticks / 1e7);
                double b = 30 + 5 * Math.Cos(DateTime.Now.Ticks / 1e7);
                
                await xi.PublishAsync("a", a / 100);
                await xi.PublishAsync("b", b / 100);

                Thread.Sleep(100);
            }
        });

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey(intercept: true);

        await xi.DisconnectAsync();
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

        var client = new XModalClient("TUNI Client", "tuni", "1.0");

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

        Console.WriteLine($" Create a 'TUNI - Monitor - slot1' ({client.DeviceType}/monitor/slot1) output node");
        Console.WriteLine(" in the XModal designer and wire it to some input node.\n");

        client.Subscribe(client.DeviceType, "monitor", "slot1", values =>
        {
            Console.WriteLine($"output/{client.DeviceType}/monitor/slot1 = {values[0]}");
        });

        client.Connected += (s, e) =>
        {
            Console.WriteLine($"Assigned as instance `{e}`\n");

            Console.WriteLine($" Activated 'TUNI - Sine' ({client.DeviceType}/periodic/sine) input node.");
            client.AttachSource(new SineSource("periodic"));

            foreach (var lmChannel in lm.Channels)
            {
                Console.WriteLine($" Activated 'TUNI - {ToHungarianStyle(lmChannel.Channel)}' ({client.DeviceType}/{lmChannel.Type}/{lmChannel.Channel}) input node.");
                client.AttachSource(lmChannel);
            }
            Console.WriteLine();

            lm.Run();
        };

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey(intercept: true);

        client.Dispose();
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
