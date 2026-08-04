using System.Net.Sockets;
using SoundTransportation.Shared;

namespace SoundTransportation.Mixer;

public sealed class UdpAudioReceiver : BackgroundService
{
    private readonly ChannelRegistry _registry;
    private readonly ILogger<UdpAudioReceiver> _logger;
    private readonly int _port;
    private readonly bool _enabled;

    public UdpAudioReceiver(ChannelRegistry registry, IConfiguration configuration, ILogger<UdpAudioReceiver> logger)
    {
        _registry = registry;
        _logger = logger;
        _port = configuration.GetValue("Audio:UdpPort", 5055);
        _enabled = configuration.GetValue("Receiver:Enabled", true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Receiver is disabled");
            return;
        }

        using var udp = new UdpClient(_port);
        _logger.LogInformation("Listening for sender audio on UDP {Port}", _port);

        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UDP receive failed");
                continue;
            }

            if (!AudioProtocol.TryGetPacketType(result.Buffer, out var type))
            {
                continue;
            }

            if (type == PacketType.Hello && AudioProtocol.TryReadHello(result.Buffer, out var hello))
            {
                _registry.UpsertHello(hello.SenderId, hello.Name, result.RemoteEndPoint.Address.ToString());
                continue;
            }

            if (type == PacketType.Audio && AudioProtocol.TryReadAudio(result.Buffer, out var audio))
            {
                _registry.PushAudio(audio.SenderId, audio.Sequence, audio.Samples, result.RemoteEndPoint.Address.ToString());
            }
        }
    }
}
