using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace SoundTransportation.Mixer;

public sealed class LocalSessionMuteService : BackgroundService
{
    private readonly ChannelRegistry _registry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalSessionMuteService> _logger;
    private readonly HashSet<int> _mutedProcessIds = [];
    private readonly int _ownProcessId = Environment.ProcessId;
    private bool _mutingActive;
    private volatile bool _remoteFocus;

    public LocalSessionMuteService(
        ChannelRegistry registry,
        IConfiguration configuration,
        ILogger<LocalSessionMuteService> logger)
    {
        _registry = registry;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var shouldMuteLocalSessions = _remoteFocus && _configuration.GetValue("Audio:LocalSessionMuteOnRemoteSolo:Enabled", true);
                if (shouldMuteLocalSessions)
                {
                    MuteLocalSessions();
                }
                else if (_mutingActive)
                {
                    RestoreLocalSessions();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update local audio session mute state");
            }

            await Task.Delay(250, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        RestoreLocalSessions();
        await base.StopAsync(cancellationToken);
    }

    public void SetRemoteFocus(bool enabled)
    {
        _remoteFocus = enabled;
        if (!enabled && _mutingActive)
        {
            RestoreLocalSessions();
        }
    }

    private void MuteLocalSessions()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = device.AudioSessionManager.Sessions;

        for (var index = 0; index < sessions.Count; index++)
        {
            using var session = sessions[index];
            var processId = (int)session.GetProcessID;
            if (processId <= 0 || processId == _ownProcessId)
            {
                continue;
            }

            if (ProcessHasExited(processId))
            {
                continue;
            }

            var volume = session.SimpleAudioVolume;
            if (volume.Mute)
            {
                continue;
            }

            volume.Mute = true;
            _mutedProcessIds.Add(processId);
        }

        _mutingActive = true;
    }

    private void RestoreLocalSessions()
    {
        if (!_mutingActive && _mutedProcessIds.Count == 0)
        {
            return;
        }

        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = device.AudioSessionManager.Sessions;

        for (var index = 0; index < sessions.Count; index++)
        {
            using var session = sessions[index];
            var processId = (int)session.GetProcessID;
            if (!_mutedProcessIds.Contains(processId))
            {
                continue;
            }

            session.SimpleAudioVolume.Mute = false;
        }

        _mutedProcessIds.Clear();
        _mutingActive = false;
    }

    private static bool ProcessHasExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }
}
