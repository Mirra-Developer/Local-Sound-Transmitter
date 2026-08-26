using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace SoundTransportation.Mixer;

public sealed class ChannelRegistry
{
    private readonly ConcurrentDictionary<Guid, AudioChannel> _channels = new();
    private readonly ConcurrentDictionary<string, Guid> _channelsBySourceIp = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _autoCreateChannels;

    public ChannelRegistry(IConfiguration configuration)
    {
        _autoCreateChannels = configuration.GetValue("Receiver:AutoCreateChannels", false);
        foreach (var section in configuration.GetSection("Receiver:Channels").GetChildren())
        {
            var sourceIp = NormalizeIp(section.GetValue<string>("SourceIp"));
            var name = section.GetValue<string>("Name") ?? sourceIp ?? "Configured Channel";
            var id = section.GetValue<Guid?>("Id") ?? CreateStableChannelId(sourceIp ?? name);
            var channel = _channels.GetOrAdd(id, _ => new AudioChannel(id, name));
            channel.Name = name;
            channel.SourceIp = sourceIp;
            channel.Volume = Math.Clamp(section.GetValue("Volume", channel.Volume), 0f, 2f);
            channel.Muted = section.GetValue("Muted", channel.Muted);
            channel.Solo = section.GetValue("Solo", channel.Solo);
            channel.OutputEnabled = section.GetValue("OutputEnabled", channel.OutputEnabled);

            if (sourceIp is not null)
            {
                _channelsBySourceIp[sourceIp] = id;
            }
        }
    }

    public IReadOnlyCollection<AudioChannel> GetChannels() => _channels.Values.OrderBy(channel => channel.Name).ToArray();

    public AudioChannel? GetChannel(Guid id) => _channels.TryGetValue(id, out var channel) ? channel : null;

    public AudioChannel? FindChannel(Guid? id, string? name, string? sourceIp)
    {
        if (id is not null && _channels.TryGetValue(id.Value, out var channelById))
        {
            return channelById;
        }

        var normalizedIp = NormalizeIp(sourceIp);
        if (normalizedIp is not null &&
            _channelsBySourceIp.TryGetValue(normalizedIp, out var channelId) &&
            _channels.TryGetValue(channelId, out var channelByIp))
        {
            return channelByIp;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            return _channels.Values.FirstOrDefault(channel =>
                channel.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    public AudioChannel? UpsertHello(Guid id, string name, string? sourceIp = null, bool forceCreate = false)
    {
        var channel = ResolveChannel(id, sourceIp, name) ?? (forceCreate ? _channels.GetOrAdd(id, _ => new AudioChannel(id, name)) : null);
        if (channel is null)
        {
            return null;
        }

        channel.Name = string.IsNullOrWhiteSpace(name) ? channel.Name : name;
        channel.LastSeenUtc = DateTimeOffset.UtcNow;
        channel.LastSourceIp = NormalizeIp(sourceIp);
        return channel;
    }

    public AudioChannel? PushAudio(Guid id, uint sequence, float[] samples, string? sourceIp = null)
    {
        var channel = ResolveChannel(id, sourceIp, $"Sender {id.ToString()[..8]}");
        if (channel is null)
        {
            return null;
        }

        channel.Push(samples, sequence, NormalizeIp(sourceIp));
        return channel;
    }

    public AudioChannel CreateChannel(string name, string? sourceIp = null)
    {
        var normalizedIp = NormalizeIp(sourceIp);
        var id = CreateStableChannelId(normalizedIp ?? name);
        var channel = _channels.GetOrAdd(id, _ => new AudioChannel(id, name));
        channel.Name = name;
        SetSourceIp(channel, normalizedIp);
        return channel;
    }

    public void UpdateChannelBinding(AudioChannel channel, string? sourceIp)
    {
        SetSourceIp(channel, NormalizeIp(sourceIp));
    }

    public void ApplyReceiverConfig(ReceiverConfigDto receiver)
    {
        _autoCreateChannels = receiver.AutoCreateChannels;
        var configuredIds = new HashSet<Guid>();

        foreach (var config in receiver.Channels)
        {
            if (string.IsNullOrWhiteSpace(config.Name) && string.IsNullOrWhiteSpace(config.SourceIp))
            {
                continue;
            }

            var channel = CreateChannel(
                string.IsNullOrWhiteSpace(config.Name) ? config.SourceIp ?? "Configured Channel" : config.Name,
                config.SourceIp);
            channel.OutputEnabled = config.OutputEnabled;
            configuredIds.Add(channel.Id);
        }

        if (!receiver.AutoCreateChannels)
        {
            RemoveUnconfiguredRemoteChannels(configuredIds);
        }
    }

    private AudioChannel? ResolveChannel(Guid senderId, string? sourceIp, string fallbackName)
    {
        var normalizedIp = NormalizeIp(sourceIp);
        if (normalizedIp is not null && _channelsBySourceIp.TryGetValue(normalizedIp, out var configuredId))
        {
            return _channels.TryGetValue(configuredId, out var configuredChannel) ? configuredChannel : null;
        }

        if (!_autoCreateChannels)
        {
            return null;
        }

        var channel = _channels.GetOrAdd(senderId, key => new AudioChannel(key, fallbackName));
        if (channel.SourceIp is null && normalizedIp is not null)
        {
            SetSourceIp(channel, normalizedIp);
        }

        return channel;
    }

    private void SetSourceIp(AudioChannel channel, string? sourceIp)
    {
        if (channel.SourceIp is not null)
        {
            _channelsBySourceIp.TryRemove(channel.SourceIp, out _);
        }

        channel.SourceIp = sourceIp;
        if (sourceIp is not null)
        {
            _channelsBySourceIp[sourceIp] = channel.Id;
        }
    }

    private void RemoveUnconfiguredRemoteChannels(HashSet<Guid> configuredIds)
    {
        foreach (var channel in _channels.Values)
        {
            if (channel.IsLocalLoopback || configuredIds.Contains(channel.Id))
            {
                continue;
            }

            if (_channels.TryRemove(channel.Id, out var removed) && removed.SourceIp is not null)
            {
                _channelsBySourceIp.TryRemove(removed.SourceIp, out _);
            }
        }
    }

    private static string? NormalizeIp(string? sourceIp)
    {
        if (string.IsNullOrWhiteSpace(sourceIp))
        {
            return null;
        }

        return IPAddress.TryParse(sourceIp.Trim(), out var parsed) ? parsed.ToString() : sourceIp.Trim();
    }

    private static Guid CreateStableChannelId(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"channel:{key}"));
        return new Guid(hash[..16]);
    }
}
