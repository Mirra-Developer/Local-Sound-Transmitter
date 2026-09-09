using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace SoundTransportation.Mixer;

public sealed class TrayService(IConfiguration configuration, IHostApplicationLifetime lifetime,
    ILogger<TrayService> logger) : IHostedService
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Control? _dispatcher;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var thread = new Thread(RunTray) { IsBackground = true, Name = "Sound Transportation tray" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return _ready.Task.WaitAsync(cancellationToken);
    }

    private void RunTray()
    {
        try
        {
            using var dispatcher = new Control();
            _ = dispatcher.Handle;
            _dispatcher = dispatcher;
            using var menu = new ContextMenuStrip();
            menu.Items.Add("\u6253\u5f00\u7ba1\u7406\u754c\u9762", null, (_, _) => OpenManager());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("\u9000\u51fa", null, (_, _) => lifetime.StopApplication());
            using var icon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Sound Transportation",
                ContextMenuStrip = menu,
                Visible = true
            };
            icon.DoubleClick += (_, _) => OpenManager();
            _ready.TrySetResult();
            Application.Run();
            icon.Visible = false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tray failed");
            _ready.TrySetException(ex);
            lifetime.StopApplication();
        }
        finally
        {
            _closed.TrySetResult();
        }
    }

    private void OpenManager()
    {
        try
        {
            var url = configuration.GetValue<string>("Ui:Url") ?? "http://127.0.0.1:5080";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to open management page");
            MessageBox.Show(ex.Message, "Sound Transportation", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_closed.Task.IsCompleted && _dispatcher is not null)
        {
            try { _dispatcher.BeginInvoke(new Action(Application.ExitThread)); }
            catch (InvalidOperationException) { }
        }
        await _closed.Task.WaitAsync(cancellationToken);
    }
}
