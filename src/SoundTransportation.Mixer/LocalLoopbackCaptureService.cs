using NAudio.Wave;
using SoundTransportation.Shared;

namespace SoundTransportation.Mixer;

public sealed class LocalLoopbackCaptureService : IHostedService, IDisposable
{
    private static readonly Guid LocalChannelId = Guid.Parse("00000000-0000-0000-0000-00000000e001");

    private readonly ChannelRegistry _registry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalLoopbackCaptureService> _logger;
    private WasapiLoopbackCapture? _capture;
    private uint _sequence;

    public LocalLoopbackCaptureService(
        ChannelRegistry registry,
        IConfiguration configuration,
        ILogger<LocalLoopbackCaptureService> logger)
    {
        _registry = registry;
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue("Audio:LocalLoopback:Enabled", true))
        {
            return Task.CompletedTask;
        }

        var name = _configuration.GetValue<string>("Audio:LocalLoopback:Name") ?? $"{Environment.MachineName} Local";
        var outputEnabled = _configuration.GetValue("Audio:LocalLoopback:OutputEnabled", false);
        var channel = _registry.UpsertHello(LocalChannelId, name, forceCreate: true);
        if (channel is null)
        {
            return Task.CompletedTask;
        }

        channel.OutputEnabled = outputEnabled;
        channel.IsLocalLoopback = true;

        _capture = new WasapiLoopbackCapture();
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += (_, eventArgs) =>
        {
            if (eventArgs.Exception is not null)
            {
                _logger.LogError(eventArgs.Exception, "Local loopback capture stopped unexpectedly");
            }
        };

        _logger.LogInformation(
            "Capturing local loopback channel '{Name}' with format {Format}; output enabled: {OutputEnabled}",
            name,
            _capture.WaveFormat,
            outputEnabled);

        _capture.StartRecording();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
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
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        if (_capture is null)
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

            var samples = normalized.AsSpan(offset, sampleCount).ToArray();
            _registry.PushAudio(LocalChannelId, _sequence++, samples);
        }
    }
}
