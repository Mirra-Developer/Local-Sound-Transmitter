using NAudio.Wave;
using SoundTransportation.Shared;

namespace SoundTransportation.Mixer;

public sealed class MixerSampleProvider : ISampleProvider
{
    private readonly ChannelRegistry _registry;

    public MixerSampleProvider(ChannelRegistry registry)
    {
        _registry = registry;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(AudioProtocol.SampleRate, AudioProtocol.Channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);

        var channels = _registry.GetChannels().ToArray();
        var frameCount = count / AudioProtocol.Channels;

        for (var frame = 0; frame < frameCount; frame++)
        {
            var mixedLeft = 0f;
            var mixedRight = 0f;

            foreach (var channel in channels)
            {
                if (!channel.TryReadFrame(out var left, out var right))
                {
                    continue;
                }

                var shouldOutput = channel.OutputEnabled && !channel.Muted;
                if (!shouldOutput)
                {
                    continue;
                }

                var gain = channel.EffectiveVolume;
                mixedLeft += left * gain;
                mixedRight += right * gain;
            }

            buffer[offset + frame * 2] = Math.Clamp(mixedLeft, -1f, 1f);
            buffer[offset + frame * 2 + 1] = Math.Clamp(mixedRight, -1f, 1f);
        }

        return count;
    }
}
