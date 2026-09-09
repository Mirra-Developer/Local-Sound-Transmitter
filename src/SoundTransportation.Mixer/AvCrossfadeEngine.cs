using System.Text.Json.Serialization;
using SoundTransportation.Shared;

namespace SoundTransportation.Mixer;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AvCrossfadeRequest([property: JsonRequired] string ProtocolVersion,
    [property: JsonRequired] Guid RequestId, [property: JsonRequired] Guid EngineInstanceId,
    [property: JsonRequired] Guid FromTrackId, [property: JsonRequired] Guid ToTrackId,
    [property: JsonRequired] long StartFrame, [property: JsonRequired] long DurationFrames,
    [property: JsonRequired] string Curve, [property: JsonRequired] float ToGain);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AvCancelRequest([property: JsonRequired] Guid EngineInstanceId);

public sealed record AvOperation(Guid RequestId, Guid EngineInstanceId, bool AudioOnly, string State,
    long StartFrame, long DurationFrames, float FromGain, float ToGain, long? RenderedThroughFrame, string? Code);

public sealed class AvException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

/// <summary>
/// One render clock and one envelope owner. All scheduling and rendering share a short
/// block-level gate; no network, device or file IO is performed while it is held.
/// The clock counts generated PCM, NOT samples already heard at the output device.
/// </summary>
public sealed class AvCrossfadeEngine(ChannelRegistry registry)
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _history = new();
    private readonly Dictionary<Guid, float> _gains = new();
    private Guid _instance = Guid.NewGuid();
    private long _nextFrame;
    private bool _outputActive;
    private Entry? _active;
    public const int LeadFrames = 4800;

    private sealed class Entry(AvCrossfadeRequest request, float fromGain)
    {
        public AvCrossfadeRequest Request { get; } = request;
        public float FromGain { get; } = fromGain;
        public string State { get; set; } = "scheduled";
        public string? Code { get; set; }
        public long? Through { get; set; }
        public AvOperation Snapshot() => new(Request.RequestId, Request.EngineInstanceId, true, State,
            Request.StartFrame, Request.DurationFrames, FromGain, Request.ToGain, Through, Code);
    }

    public object Capabilities()
    {
        lock (_gate) return new { protocolVersion = "1.0", contractRevision = "1.0.0-draft.1",
            service = "sound-transmitter", engineInstanceId = _instance,
            features = new { audioCrossfade = true, sampleAccurateEnvelope = true, videoTransitions = false,
                localPcmIngress = false, avSynchronization = false, hardwarePresentationFeedback = false } };
    }

    public object Clock()
    {
        lock (_gate) return new { protocolVersion = "1.0", engineInstanceId = _instance,
            sampleRate = AudioProtocol.SampleRate, channels = AudioProtocol.Channels,
            nextFrame = _nextFrame, outputActive = _outputActive, clockKind = "renderedAudioFrames" };
    }

    public Guid InstanceId { get { lock (_gate) return _instance; } }
    public long NextFrame { get { lock (_gate) return _nextFrame; } }

    public object Tracks()
    {
        lock (_gate) return new { protocolVersion = "1.0", engineInstanceId = _instance,
            tracks = registry.GetChannels().Select(c => new { audioTrackId = c.Id, c.Name, c.SourceIp,
                c.IsLocalLoopback, queuedFrames = c.QueuedSamples / 2,
                audioReady = Ready(c), gain = Gain(c) }).ToArray() };
    }

    public void ResetOutput(bool active)
    {
        lock (_gate)
        {
            // Device recreation invalidates every frame number and retry ID.
            _instance = Guid.NewGuid();
            _nextFrame = 0;
            _outputActive = active;
            _active = null;
            _history.Clear();
            _gains.Clear();
        }
    }

    public void OutputStopped()
    {
        lock (_gate)
        {
            _outputActive = false;
            if (_active is not null) Fail(_active, "OUTPUT_STOPPED", _nextFrame);
        }
    }

    public AvOperation Schedule(AvCrossfadeRequest request)
    {
        lock (_gate)
        {
            CheckInstance(request.EngineInstanceId);
            if (request.ProtocolVersion != "1.0") throw Error(422, "UNSUPPORTED_VERSION");
            if (request.RequestId == Guid.Empty || request.FromTrackId == Guid.Empty ||
                request.ToTrackId == Guid.Empty || request.FromTrackId == request.ToTrackId ||
                request.Curve is not ("linear" or "equalPower") ||
                !float.IsFinite(request.ToGain) || request.ToGain < 0 || request.ToGain > 1 ||
                request.DurationFrames < 1 || request.DurationFrames > 2_880_000 ||
                request.StartFrame < 0 || request.StartFrame > 9_007_199_251_860_991L)
                throw Error(422, "INVALID_REQUEST");
            if (_history.TryGetValue(request.RequestId, out var previous))
            {
                if (previous.Request != request) throw Error(409, "REQUEST_ID_CONFLICT");
                return previous.Snapshot();
            }
            if (!_outputActive) throw Error(503, "OUTPUT_UNAVAILABLE");
            if (_active is not null) throw Error(409, "TRANSITION_BUSY");
            if (_history.Count >= 4096) throw Error(409, "REQUEST_HISTORY_FULL");
            if (request.StartFrame < _nextFrame + LeadFrames || request.StartFrame > _nextFrame + 240_000)
                throw Error(422, "INVALID_START_FRAME");
            var from = registry.GetChannel(request.FromTrackId) ?? throw Error(404, "TRACK_NOT_FOUND");
            var to = registry.GetChannel(request.ToTrackId) ?? throw Error(404, "TRACK_NOT_FOUND");
            if (from.IsLocalLoopback || to.IsLocalLoopback) throw Error(422, "LOOPBACK_NOT_SUPPORTED");
            if (!Ready(from) || !Ready(to)) throw Error(503, "TRACK_NOT_READY");
            var fromGain = Gain(from);
            if (fromGain <= 0 || Gain(to) > 0) throw Error(409, "INVALID_GAIN_STATE");
            _active = new Entry(request, fromGain);
            _history.Add(request.RequestId, _active);
            return _active.Snapshot();
        }
    }

    public AvOperation Get(Guid requestId, Guid instance)
    {
        lock (_gate)
        {
            CheckInstance(instance);
            return _history.TryGetValue(requestId, out var entry) ? entry.Snapshot() : throw Error(404, "REQUEST_NOT_FOUND");
        }
    }

    public AvOperation Cancel(Guid requestId, Guid instance)
    {
        lock (_gate)
        {
            CheckInstance(instance);
            if (!_history.TryGetValue(requestId, out var entry)) throw Error(404, "REQUEST_NOT_FOUND");
            if (entry.State == "running") throw Error(409, "ALREADY_RUNNING");
            if (entry.State == "scheduled")
            {
                entry.State = "cancelled";
                _active = null;
            }
            return entry.Snapshot();
        }
    }

    public int Render(float[] buffer, int offset, int count)
    {
        if (count % 2 != 0) throw new ArgumentException("Stereo render count must be even.");
        lock (_gate)
        {
            Array.Clear(buffer, offset, count);
            if (!_outputActive) return count;
            var channels = registry.GetChannels().ToArray();
            // Scratch allocation is once per callback, not once per sample.
            var left = new float[channels.Length];
            var right = new float[channels.Length];
            var hasFrame = new bool[channels.Length];
            for (var i = 0; i < count / 2; i++)
            {
                var frame = _nextFrame++;
                var entry = _active;
                if (entry is not null && frame >= entry.Request.StartFrame && entry.State == "scheduled")
                {
                    var target = registry.GetChannel(entry.Request.ToTrackId);
                    if (target is null || !Ready(target)) Fail(entry, "TARGET_NOT_READY_AT_START", frame);
                    else entry.State = "running";
                }
                for (var c = 0; c < channels.Length; c++)
                    hasFrame[c] = channels[c].TryReadFrame(out left[c], out right[c]);

                entry = _active;
                if (entry?.State == "running")
                {
                    var targetIndex = Array.FindIndex(channels, c => c.Id == entry.Request.ToTrackId);
                    if (targetIndex < 0 || !hasFrame[targetIndex]) Fail(entry, "TARGET_UNDERRUN", frame);
                    else
                    {
                        var p = Math.Clamp((double)(frame - entry.Request.StartFrame) / entry.Request.DurationFrames, 0, 1);
                        var (a, b) = Envelope(entry.Request.Curve, p);
                        _gains[entry.Request.FromTrackId] = entry.FromGain * a;
                        _gains[entry.Request.ToTrackId] = entry.Request.ToGain * b;
                        entry.Through = frame;
                        if (p >= 1)
                        {
                            // Avoid tiny cos(pi/2) residuals becoming an audible-state conflict.
                            _gains[entry.Request.FromTrackId] = 0;
                            _gains[entry.Request.ToTrackId] = entry.Request.ToGain;
                            entry.State = "rendered";
                            _active = null;
                        }
                    }
                }

                var mixedLeft = 0f;
                var mixedRight = 0f;
                for (var c = 0; c < channels.Length; c++)
                {
                    if (!hasFrame[c]) continue;
                    var gain = Gain(channels[c]);
                    mixedLeft += left[c] * gain;
                    mixedRight += right[c] * gain;
                }
                buffer[offset + i * 2] = Math.Clamp(mixedLeft, -1, 1);
                buffer[offset + i * 2 + 1] = Math.Clamp(mixedRight, -1, 1);
            }
            return count;
        }
    }

    public static (float From, float To) Envelope(string curve, double p) => curve == "equalPower"
        ? ((float)Math.Cos(p * Math.PI / 2), (float)Math.Sin(p * Math.PI / 2))
        : ((float)(1 - p), (float)p);

    private float Gain(AudioChannel c) => _gains.TryGetValue(c.Id, out var value) ? value
        : c.Muted || !c.OutputEnabled ? 0 : c.EffectiveVolume;
    private static bool Ready(AudioChannel c) => c.QueuedSamples >= 2 && c.HasFreshAudio(TimeSpan.FromMilliseconds(500));
    private void CheckInstance(Guid instance) { if (instance != _instance) throw Error(409, "INSTANCE_CHANGED"); }
    private static AvException Error(int status, string code) => new(status, code, code);
    private void Fail(Entry entry, string code, long frame)
    {
        _gains[entry.Request.FromTrackId] = entry.FromGain;
        _gains[entry.Request.ToTrackId] = 0;
        entry.State = "failed";
        entry.Code = code;
        entry.Through = frame;
        _active = null;
    }
}
