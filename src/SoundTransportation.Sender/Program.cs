using System.Net;
using System.Net.Sockets;
using NAudio.Wave;
using SoundTransportation.Shared;

var options = SenderOptions.Parse(args);
if (options.ShowHelp)
{
    SenderOptions.PrintHelp();
    return;
}

using var udp = new UdpClient();
udp.Connect(options.ServerAddress, options.Port);

var senderId = options.SenderId ?? Guid.NewGuid();
var helloPacket = AudioProtocol.WriteHello(senderId, options.Name);
var sequence = 0u;

using var capture = new WasapiLoopbackCapture();
using var helloTimer = new Timer(_ => SendHello(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopped.TrySetResult();
};

capture.DataAvailable += (_, eventArgs) =>
{
    var normalized = SampleNormalizer.ToTransportFormat(capture.WaveFormat, eventArgs.Buffer.AsSpan(0, eventArgs.BytesRecorded));
    for (var offset = 0; offset < normalized.Length; offset += AudioProtocol.MaxFramesPerPacket * AudioProtocol.Channels)
    {
        var sampleCount = Math.Min(AudioProtocol.MaxFramesPerPacket * AudioProtocol.Channels, normalized.Length - offset);
        if (sampleCount <= 0)
        {
            continue;
        }

        var packet = AudioProtocol.WriteAudio(senderId, sequence++, normalized.AsSpan(offset, sampleCount));
        udp.Send(packet, packet.Length);
    }
};

capture.RecordingStopped += (_, eventArgs) =>
{
    if (eventArgs.Exception is not null)
    {
        Console.Error.WriteLine(eventArgs.Exception);
    }

    stopped.TrySetResult();
};

Console.WriteLine($"Sender: {options.Name}");
Console.WriteLine($"Id: {senderId}");
Console.WriteLine($"Target: {options.ServerAddress}:{options.Port}");
Console.WriteLine($"Capture format: {capture.WaveFormat}");
Console.WriteLine("Press Ctrl+C to stop.");

capture.StartRecording();
await stopped.Task;
capture.StopRecording();

void SendHello()
{
    try
    {
        udp.Send(helloPacket, helloPacket.Length);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to send hello: {ex.Message}");
    }
}

internal sealed record SenderOptions(IPAddress ServerAddress, int Port, string Name, Guid? SenderId, bool ShowHelp)
{
    public static SenderOptions Parse(string[] args)
    {
        var server = IPAddress.Loopback;
        var port = 5055;
        var name = Environment.MachineName;
        Guid? senderId = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "-h" or "--help")
            {
                return new SenderOptions(server, port, name, senderId, true);
            }

            string RequireValue()
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"Missing value for {arg}.");
                }

                return args[++index];
            }

            switch (arg)
            {
                case "--server":
                    server = IPAddress.Parse(RequireValue());
                    break;
                case "--port":
                    port = int.Parse(RequireValue());
                    break;
                case "--name":
                    name = RequireValue();
                    break;
                case "--id":
                    senderId = Guid.Parse(RequireValue());
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return new SenderOptions(server, port, name, senderId, false);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
        SoundTransportation.Sender

        Captures this computer's default playback mix and sends it to the mixer.

        Options:
          --server <ip>   Mixer computer IP address. Default: 127.0.0.1
          --port <port>   Mixer UDP port. Default: 5055
          --name <name>   Channel name shown on the mixer. Default: machine name
          --id <guid>     Stable sender id. Optional.
        """);
    }
}
