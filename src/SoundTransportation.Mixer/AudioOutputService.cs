using NAudio.Wave;

namespace SoundTransportation.Mixer;

public sealed class AudioOutputService : IHostedService, IDisposable
{
    private readonly ChannelRegistry _registry;
    private readonly IConfiguration _configuration;
    private WaveOutEvent? _output;
    private readonly AvCrossfadeEngine _avEngine;

    public AudioOutputService(ChannelRegistry registry, IConfiguration configuration, AvCrossfadeEngine avEngine)
    {
        _registry = registry;
        _configuration = configuration;
        _avEngine = avEngine;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Reload();
        return Task.CompletedTask;
    }

    public void Reload()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _avEngine.ResetOutput(false);
        if (!_configuration.GetValue("Audio:Output:Enabled", true)) return;

        var integrated = AvApi.Enabled(_configuration);
        var provider = new MixerSampleProvider(_registry, integrated ? _avEngine : null);
        _output = new WaveOutEvent
        {
            DesiredLatency = 100
        };
        _output.Init(provider);
        if (integrated)
        {
            _output.PlaybackStopped += (_, _) => _avEngine.OutputStopped();
            _avEngine.ResetOutput(true);
        }
        _output.Play();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _output?.Stop();
        _avEngine.OutputStopped();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _output?.Dispose();
    }
}
