using System.Net.Sockets;
using SoundTransportation.Shared;

namespace SoundTransportation.Mixer;

public sealed class UdpAudioReceiver : IHostedService, IDisposable
{
    private readonly ChannelRegistry _registry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UdpAudioReceiver> _logger;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;

    public UdpAudioReceiver(ChannelRegistry registry, IConfiguration configuration, ILogger<UdpAudioReceiver> logger)
    {
        _registry = registry;
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Reload();
        return Task.CompletedTask;
    }

    public void Reload()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        _runTask = RunAsync(_runCts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _runCts?.Cancel();
        if (_runTask is not null)
        {
            await _runTask.WaitAsync(cancellationToken);
        }
    }

    public void Dispose() => _runCts?.Dispose();

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue("Receiver:Enabled", true);
        var port = _configuration.GetValue("Audio:UdpPort", 5055);
        if (!enabled)
        {
            _logger.LogInformation("Receiver is disabled");
            return;
        }

        using var udp = new UdpClient(port);
        _logger.LogInformation("Listening for sender audio on UDP {Port}", port);

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
