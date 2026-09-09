# Mirraflow audio integration — P1

Contract: [docs/mirra-av-contract-v1.md](docs/mirra-av-contract-v1.md).

Cross-repository ownership and rollout sequence: [joint handoff](https://github.com/Mirra-Developer/mirra-lobby-hub/blob/codex/av-contract-v1/docs/av-integration-handoff.md).
The Lobby repository is the editing source for the contract and schema; keep
both copies in docs identical. This branch was developed in a separate clone;
fetch and review it on the Sound development PC before merging its newer work.

The normal tray application is unchanged unless `AvIntegration__Enabled=true` is
set before launch. Integration mode provides `/api/av/v1` and requires the local
`MIRRA_AV_TOKEN` environment variable. Keep tokens out of repository config and UI.

In this mode:
- Crossfades are computed per stereo sample frame in the real MixerSampleProvider path.
- The old 20 ms fade task, local system-session muting and local speaker loopback
  capture do not start. Legacy `/api` writes return 409; configure channel bindings
  before enabling integration.
- UDP receiver/track bindings continue to work. A starts audible and B starts
  muted/zero gain with PCM still flowing; the new engine owns subsequent A/B gains.
- Exactly one A/B transition can be active. Other tracks continue unchanged.
- Device output recreation invalidates the engine instance and sample clock.
- `rendered` means PCM generated, not hardware playback acknowledged.

Not implemented in P1: local-file audio ingest, Lobby audio ingest, device playhead
feedback, precise NDI/audio synchronization, shared texture capture, AV scene take.
Do not deploy P1 as a completed broadcast mixer. The existing hard sample clamp
is not a limiter, and target-loss recovery may cause a gain discontinuity.

Run hardware-free tests on a .NET 8+ SDK host:

```powershell
dotnet run --project tests/SoundTransportation.Av.Tests
dotnet build src/SoundTransportation.Mixer -c Release
```

The test host `--serve` binds loopback port 18550 and exposes test-only PCM pumping
under `/api/av/v1/test`. It does not load Windows capture, speaker output or tray.
These test routes are never included in the production program.
