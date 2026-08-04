using System.Collections.Concurrent;

namespace SoundTransportation.Mixer;

public sealed class AudioChannel
{
    private const int MaxQueuedSamples = 48_000 * 2 * 2;

    private readonly ConcurrentQueue<float[]> _chunks = new();
    private readonly object _readLock = new();
    private float[]? _currentChunk;
    private int _currentOffset;
    private int _queuedSamples;
    private float _level;

    public AudioChannel(Guid id, string name)
    {
        Id = id;
        Name = name;
        LastSeenUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; }
    public string Name { get; set; }
    public string? SourceIp { get; set; }
    public string? LastSourceIp { get; set; }
    public bool IsLocalLoopback { get; set; }
    public float Volume { get; set; } = 1f;
    public bool Muted { get; set; }
    public bool Solo { get; set; }
    public bool OutputEnabled { get; set; } = true;
    public DateTimeOffset LastSeenUtc { get; set; }
    public uint LastSequence { get; set; }

    public int QueuedSamples => Volatile.Read(ref _queuedSamples);
    public float Level => Volatile.Read(ref _level);

    public void Push(float[] samples, uint sequence, string? sourceIp = null)
    {
        if (samples.Length == 0)
        {
            return;
        }

        LastSeenUtc = DateTimeOffset.UtcNow;
        LastSequence = sequence;
        LastSourceIp = sourceIp;
        UpdateLevel(samples);

        while (QueuedSamples > MaxQueuedSamples && _chunks.TryDequeue(out var dropped))
        {
            Interlocked.Add(ref _queuedSamples, -dropped.Length);
        }

        _chunks.Enqueue(samples);
        Interlocked.Add(ref _queuedSamples, samples.Length);
    }

    public bool TryReadFrame(out float left, out float right)
    {
        lock (_readLock)
        {
            while (_currentChunk is null || _currentOffset + 1 >= _currentChunk.Length)
            {
                if (!_chunks.TryDequeue(out _currentChunk))
                {
                    left = 0f;
                    right = 0f;
                    return false;
                }

                _currentOffset = 0;
            }

            left = _currentChunk[_currentOffset++];
            right = _currentChunk[_currentOffset++];
            Interlocked.Add(ref _queuedSamples, -2);
            return true;
        }
    }

    private void UpdateLevel(float[] samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        var current = Volatile.Read(ref _level);
        Volatile.Write(ref _level, Math.Max(peak, current * 0.85f));
    }
}
