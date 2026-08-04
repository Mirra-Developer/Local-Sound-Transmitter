using NAudio.Wave;

namespace SoundTransportation.Mixer;

public sealed class AudioOutputService : IHostedService, IDisposable
{
    private readonly ChannelRegistry _registry;
    private readonly IConfiguration _configuration;
    private WaveOutEvent? _output;

    public AudioOutputService(ChannelRegistry registry, IConfiguration configuration)
    {
        _registry = registry;
        _configuration = configuration;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue("Audio:Output:Enabled", true))
        {
            return Task.CompletedTask;
        }

        var provider = new MixerSampleProvider(_registry);
        _output = new WaveOutEvent
        {
            DesiredLatency = 100
        };
        _output.Init(provider);
        _output.Play();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _output?.Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _output?.Dispose();
    }
}
