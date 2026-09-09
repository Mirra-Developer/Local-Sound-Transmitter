using SoundTransportation.Mixer;

if (args.Contains("--serve"))
{
    // Test-only host: no WASAPI, no tray, no sender, no live hardware. It exposes
    // the actual production AvApi and renders PCM only on explicit test requests.
    var builder = WebApplication.CreateBuilder(args.Where(a => a != "--serve").ToArray());
    builder.WebHost.UseUrls("http://127.0.0.1:18550");
    builder.Configuration["AvIntegration:Enabled"] = "true";
    var fixture = Fixture.Create();
    builder.Services.AddSingleton(fixture.Registry);
    builder.Services.AddSingleton(fixture.Engine);
    var app = builder.Build();
    app.MapAvApi();
    app.MapPost("/api/av/v1/test/render", (int frames) =>
    {
        if (frames < 1 || frames > 100_000) return Results.BadRequest();
        fixture.Feed(120_000);
        fixture.Engine.Render(new float[frames * 2], 0, frames * 2);
        return Results.Ok(fixture.Engine.Clock());
    });
    app.MapGet("/api/av/v1/test/fixture", () => new { fromTrackId = fixture.A.Id, toTrackId = fixture.B.Id });
    app.Run();
    return;
}

var passed = 0;
void Test(string name, Action test)
{
    test();
    passed++;
    Console.WriteLine("PASS " + name);
}
static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
static void Near(float actual, double expected) => Assert(Math.Abs(actual - expected) < 0.00002, $"{actual} != {expected}");
static void Reject(string code, Action call)
{
    try { call(); throw new Exception("Expected " + code); }
    catch (AvException e) { Assert(e.Code == code, $"{e.Code} != {code}"); }
}

Test("equal-power overlaps A/B and preserves unrelated sound", () =>
{
    var f = Fixture.Create();
    var request = f.Request("equalPower");
    f.Engine.Schedule(request);
    var pcm = f.Render(52801);
    // A occupies left, B right, unrelated effect contributes .05 to both.
    Near(pcm[0], .25); Near(pcm[1], .05);
    var middle = (int)(request.StartFrame + request.DurationFrames / 2) * 2;
    Near(pcm[middle], .05 + .2 / Math.Sqrt(2));
    Near(pcm[middle + 1], .05 + .2 / Math.Sqrt(2));
    Near(pcm[^2], .05); Near(pcm[^1], .25);
    Assert(f.Engine.Get(request.RequestId, f.Engine.InstanceId).State == "rendered", "not rendered");
});
Test("linear ramp and exact endpoint", () =>
{
    var f = Fixture.Create(); var r = f.Request("linear"); f.Engine.Schedule(r);
    var pcm = f.Render(52801); var mid = 28800 * 2;
    Near(pcm[mid], .15); Near(pcm[mid + 1], .15); Near(pcm[^2], .05); Near(pcm[^1], .25);
});
Test("envelope independent of render block sizes", () =>
{
    var a = Fixture.Create(); var b = Fixture.Create();
    a.Engine.Schedule(a.Request()); b.Engine.Schedule(b.Request());
    var expected = a.Render(52801); var actual = new List<float>();
    var remaining = 52801;
    while (remaining > 0) { var n = Math.Min(remaining, 127); actual.AddRange(b.Render(n)); remaining -= n; }
    Assert(expected.SequenceEqual(actual), "block boundary changed the envelope");
});
Test("idempotency before and after rendering; conflicting retry rejected", () =>
{
    var f = Fixture.Create(); var r = f.Request(); var first = f.Engine.Schedule(r);
    Assert(f.Engine.Schedule(r) == first, "repeated request changed");
    Reject("REQUEST_ID_CONFLICT", () => f.Engine.Schedule(r with { ToGain = .5f }));
    f.Render(52801); Assert(f.Engine.Schedule(r).State == "rendered", "retry restarted");
});
Test("competing command rejected without interrupting A/B", () =>
{
    var f = Fixture.Create(); var r = f.Request(); f.Engine.Schedule(r);
    Reject("TRANSITION_BUSY", () => f.Engine.Schedule(r with { RequestId = Guid.NewGuid() }));
    Assert(f.Engine.Get(r.RequestId, f.Engine.InstanceId).State == "scheduled", "interrupted");
});
Test("cancel scheduled only; cancelled command never executes", () =>
{
    var f = Fixture.Create(); var r = f.Request(); f.Engine.Schedule(r);
    f.Engine.Cancel(r.RequestId, f.Engine.InstanceId); var pcm = f.Render(52801);
    Near(pcm[^2], .25); Near(pcm[^1], .05);
    Assert(f.Engine.Schedule(r).State == "cancelled", "cancelled replayed");
});
Test("running cancellation rejected", () =>
{
    var f = Fixture.Create(); var r = f.Request(); f.Engine.Schedule(r); f.Render(4801);
    Reject("ALREADY_RUNNING", () => f.Engine.Cancel(r.RequestId, f.Engine.InstanceId));
});
Test("restart invalidates old frame clock and request IDs", () =>
{
    var f = Fixture.Create(); var r = f.Request(); f.Engine.Schedule(r); f.Engine.ResetOutput(true);
    Reject("INSTANCE_CHANGED", () => f.Engine.Schedule(r));
    Reject("INSTANCE_CHANGED", () => f.Engine.Get(r.RequestId, r.EngineInstanceId));
});
Test("missing output, target, late start and invalid values rejected", () =>
{
    var f = Fixture.Create(); var r = f.Request();
    Reject("INVALID_START_FRAME", () => f.Engine.Schedule(r with { StartFrame = 0 }));
    Reject("INVALID_REQUEST", () => f.Engine.Schedule(r with { ToGain = float.NaN }));
    Reject("INVALID_REQUEST", () => f.Engine.Schedule(r with { DurationFrames = 0 }));
    Reject("UNSUPPORTED_VERSION", () => f.Engine.Schedule(r with { ProtocolVersion = "2.0" }));
    Reject("TRACK_NOT_FOUND", () => f.Engine.Schedule(r with { ToTrackId = Guid.NewGuid() }));
    f.Engine.OutputStopped(); Reject("OUTPUT_UNAVAILABLE", () => f.Engine.Schedule(r));
});
Test("Hello does not prove PCM readiness", () =>
{
    var f = Fixture.Create(); var empty = f.Registry.CreateChannel("hello-only"); empty.Muted = true;
    Reject("TRACK_NOT_READY", () => f.Engine.Schedule(f.Request() with { ToTrackId = empty.Id }));
});
Test("audible B and local loopback rejected", () =>
{
    var f = Fixture.Create(); f.B.Muted = false;
    Reject("INVALID_GAIN_STATE", () => f.Engine.Schedule(f.Request()));
    f.B.IsLocalLoopback = true; Reject("LOOPBACK_NOT_SUPPORTED", () => f.Engine.Schedule(f.Request()));
});
Test("target drained before start retains A", () =>
{
    var f = Fixture.Create(); var r = f.Request(); f.Engine.Schedule(r);
    while (f.B.TryReadFrame(out _, out _)) { }
    var pcm = f.Render(4801); Near(pcm[^2], .25); Near(pcm[^1], .05);
    Assert(f.Engine.Get(r.RequestId, f.Engine.InstanceId).Code == "TARGET_NOT_READY_AT_START", "wrong failure");
});
Test("target underrun mid-fade fails and retains A", () =>
{
    var f = Fixture.Create(); var r = f.Request(); f.Engine.Schedule(r); f.Render(5000);
    while (f.B.TryReadFrame(out _, out _)) { }
    var pcm = f.Render(1); Near(pcm[0], .25); Near(pcm[1], .05);
    Assert(f.Engine.Get(r.RequestId, f.Engine.InstanceId).Code == "TARGET_UNDERRUN", "wrong failure");
});
Test("output stopped fails operation rather than reporting completion", () =>
{
    var f = Fixture.Create(); var r = f.Request(); f.Engine.Schedule(r); f.Engine.OutputStopped();
    Assert(f.Engine.Get(r.RequestId, r.EngineInstanceId).Code == "OUTPUT_STOPPED", "missing failure");
});
Console.WriteLine($"{passed} audio contract tests passed; offline PCM only, no speaker output.");

sealed class Fixture
{
    public required ChannelRegistry Registry { get; init; }
    public required AvCrossfadeEngine Engine { get; init; }
    public required AudioChannel A { get; init; }
    public required AudioChannel B { get; init; }
    public required AudioChannel Fx { get; init; }
    public static Fixture Create()
    {
        var registry = new ChannelRegistry(new ConfigurationBuilder().Build());
        var fixture = new Fixture { Registry = registry, Engine = new AvCrossfadeEngine(registry),
            A = registry.CreateChannel("fixture-a"), B = registry.CreateChannel("fixture-b"), Fx = registry.CreateChannel("fixture-fx") };
        fixture.B.Muted = true; fixture.Feed(120000); fixture.Engine.ResetOutput(true); return fixture;
    }
    public void Feed(int frames)
    {
        static float[] Signal(int frames, float l, float r)
        {
            var pcm = new float[frames * 2];
            for (var i = 0; i < frames; i++) { pcm[i * 2] = l; pcm[i * 2 + 1] = r; }
            return pcm;
        }
        A.Push(Signal(frames, .2f, 0), 1); B.Push(Signal(frames, 0, .2f), 1); Fx.Push(Signal(frames, .05f, .05f), 1);
    }
    public AvCrossfadeRequest Request(string curve = "equalPower") => new("1.0", Guid.NewGuid(), Engine.InstanceId,
        A.Id, B.Id, Engine.NextFrame + 4800, 48000, curve, 1);
    public float[] Render(int frames) { var pcm = new float[frames * 2]; Engine.Render(pcm, 0, pcm.Length); return pcm; }
}
