using System.Diagnostics;

namespace SoundTransportation.Mixer;

public sealed class BrowserLauncherService : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<BrowserLauncherService> _logger;

    public BrowserLauncherService(
        IConfiguration configuration,
        IHostApplicationLifetime lifetime,
        ILogger<BrowserLauncherService> logger)
    {
        _configuration = configuration;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue("Ui:AutoOpen", true))
        {
            return Task.CompletedTask;
        }

        var url = _configuration.GetValue<string>("Ui:Url") ?? "http://127.0.0.1:5080";
        _lifetime.ApplicationStarted.Register(() => OpenBrowser(url));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open browser UI at {Url}", url);
        }
    }
}
