using System.Net;
using System.Net.Sockets;
using NAudio.Wave;
using SoundTransportation.Shared;

namespace SoundTransportation.Mixer;

public sealed class IntegratedSenderService : IHostedService, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<IntegratedSenderService> _logger;
    private readonly List<UdpClient> _targets = [];
    private WasapiLoopbackCapture? _capture;
    private Timer? _helloTimer;
    private Guid _senderId;
    private string _name = Environment.MachineName;
    private byte[] _helloPacket = [];
    private uint _sequence;

    public IntegratedSenderService(IConfiguration configuration, ILogger<IntegratedSenderService> logger)
    {
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
        StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        foreach (var target in _targets) target.Dispose();
        _targets.Clear();
        if (!_configuration.GetValue("Transmitter:Enabled", false)) return;

        _name = _configuration.GetValue<string>("Transmitter:Name") ?? Environment.MachineName;
        _senderId = ReadSenderId();
        _helloPacket = AudioProtocol.WriteHello(_senderId, _name);

        foreach (var target in ReadTargets())
        {
            var udp = new UdpClient();
            udp.Connect(target.Address, target.Port);
            _targets.Add(udp);
            _logger.LogInformation("Sending local audio to {Address}:{Port}", target.Address, target.Port);
        }

        if (_targets.Count == 0)
        {
            _logger.LogWarning("Transmitter is enabled but no valid targets are configured");
            return;
        }

        _capture = new WasapiLoopbackCapture();
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += (_, eventArgs) =>
        {
            if (eventArgs.Exception is not null)
            {
                _logger.LogError(eventArgs.Exception, "Integrated sender capture stopped unexpectedly");
            }
        };

        _helloTimer = new Timer(_ => SendToTargets(_helloPacket), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        _logger.LogInformation("Transmitter '{Name}' started with id {SenderId}; capture format {Format}", _name, _senderId, _capture.WaveFormat);
        _capture.StartRecording();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _helloTimer?.Dispose();
        _capture?.StopRecording();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.Dispose();
        }

        _helloTimer?.Dispose();
        foreach (var target in _targets)
        {
            target.Dispose();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        if (_capture is null || _targets.Count == 0)
        {
            return;
        }

        var normalized = SampleNormalizer.ToTransportFormat(_capture.WaveFormat, eventArgs.Buffer.AsSpan(0, eventArgs.BytesRecorded));
        for (var offset = 0; offset < normalized.Length; offset += AudioProtocol.MaxFramesPerPacket * AudioProtocol.Channels)
        {
            var sampleCount = Math.Min(AudioProtocol.MaxFramesPerPacket * AudioProtocol.Channels, normalized.Length - offset);
            if (sampleCount <= 0)
            {
                continue;
            }

            var packet = AudioProtocol.WriteAudio(_senderId, _sequence++, normalized.AsSpan(offset, sampleCount));
            SendToTargets(packet);
        }
    }

    private void SendToTargets(byte[] packet)
    {
        foreach (var target in _targets)
        {
            try
            {
                target.Send(packet, packet.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send transmitter packet");
            }
        }
    }

    private Guid ReadSenderId()
    {
        var configured = _configuration.GetValue<string>("Transmitter:SenderId");
        return Guid.TryParse(configured, out var senderId) ? senderId : Guid.NewGuid();
    }

    private IEnumerable<SenderTarget> ReadTargets()
    {
        foreach (var section in _configuration.GetSection("Transmitter:Targets").GetChildren())
        {
            var address = section.GetValue<string>("Address");
            var port = section.GetValue("Port", _configuration.GetValue("Audio:UdpPort", 5055));
            if (IPAddress.TryParse(address, out var ipAddress))
            {
                yield return new SenderTarget(ipAddress, port);
            }
        }
    }

    private sealed record SenderTarget(IPAddress Address, int Port);
}
