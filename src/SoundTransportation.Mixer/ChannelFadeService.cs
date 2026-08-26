using System.Collections.Concurrent;

namespace SoundTransportation.Mixer;

public sealed class ChannelFadeService : BackgroundService
{
    private readonly ChannelRegistry _registry;
    private readonly ConcurrentDictionary<Guid, FadeState> _fades = new();

    public ChannelFadeService(ChannelRegistry registry)
    {
        _registry = registry;
    }

    public void FadeTo(Guid channelId, float targetVolume, TimeSpan duration, bool setMutedAtEnd = false)
    {
        var channel = _registry.GetChannel(channelId);
        if (channel is null)
        {
            return;
        }

        var clampedTarget = Math.Clamp(targetVolume, 0f, 2f);
        if (duration <= TimeSpan.Zero)
        {
            channel.EffectiveVolume = clampedTarget;
            channel.Volume = clampedTarget;
            channel.Muted = setMutedAtEnd || clampedTarget <= 0f;
            _fades.TryRemove(channelId, out _);
            return;
        }

        if (clampedTarget > 0f)
        {
            channel.Muted = false;
            channel.OutputEnabled = true;
        }

        _fades[channelId] = new FadeState(
            channelId,
            channel.EffectiveVolume,
            clampedTarget,
            DateTimeOffset.UtcNow,
            duration,
            setMutedAtEnd);
    }

    public void FocusChannel(Guid channelId, float activeVolume, TimeSpan duration)
    {
        foreach (var channel in _registry.GetChannels())
        {
            var isActive = channel.Id == channelId;
            channel.Solo = false;
            FadeTo(channel.Id, isActive ? activeVolume : 0f, duration, setMutedAtEnd: !isActive);
            channel.OutputEnabled = true;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            Tick();
        }
    }

    private void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var state in _fades.Values)
        {
            var channel = _registry.GetChannel(state.ChannelId);
            if (channel is null)
            {
                _fades.TryRemove(state.ChannelId, out _);
                continue;
            }

            var elapsed = now - state.StartedAt;
            var progress = Math.Clamp(elapsed.TotalMilliseconds / state.Duration.TotalMilliseconds, 0d, 1d);
            var eased = SmoothStep((float)progress);
            var volume = state.StartVolume + (state.TargetVolume - state.StartVolume) * eased;
            channel.EffectiveVolume = volume;

            if (progress >= 1d)
            {
                channel.Volume = state.TargetVolume;
                channel.EffectiveVolume = state.TargetVolume;
                channel.Muted = state.MuteAtEnd || state.TargetVolume <= 0f;
                _fades.TryRemove(state.ChannelId, out _);
            }
        }
    }

    private static float SmoothStep(float value)
    {
        return value * value * (3f - 2f * value);
    }

    private sealed record FadeState(
        Guid ChannelId,
        float StartVolume,
        float TargetVolume,
        DateTimeOffset StartedAt,
        TimeSpan Duration,
        bool MuteAtEnd);
}
