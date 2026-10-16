using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace QdsModManager.App;

internal sealed class TrayLifetime : IDisposable
{
    private readonly Application app;
    private readonly MainWindow window;
    private readonly Icon icon;
    private readonly Forms.NotifyIcon tray;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly DispatcherTimer timer;
    private DateTimeOffset nextCheck = DateTimeOffset.UtcNow.AddHours(6);
    private bool exiting, disposed;
    internal bool Visible => tray.Visible;
    internal TrayLifetime(Application app, MainWindow window)
    {
        this.app = app; this.window = window;
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/" + typeof(MainWindow).Assembly.GetName().Name + ";component/Assets/qmm.ico"))!.Stream;
        icon = new Icon(stream);
        tray = new Forms.NotifyIcon { Icon = icon, Text = AppIdentity.Name, ContextMenuStrip = menu };
        menu.Items.Add("Open QMM", null, (_, _) => Show());
        var check = menu.Items.Add("Check for updates", null, async (_, _) => { Show(); await window.CheckAllUpdates(); });
        var launch = menu.Items.Add("Launch Designer", null, (_, _) => { if (!window.LaunchFromTray()) Show(); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        var exit = menu.Items.Add("Exit", null, (_, _) => Exit());
        menu.Opening += (_, _) => { check.Enabled = !window.WorkInProgress; launch.Enabled = window.CanLaunchFromTray; exit.Enabled = !window.ApplyingChange; };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Show(); };
        tray.BalloonTipClicked += (_, _) => { Show(); window.OpenUpdateSettings(); };
        window.Closing += Closing;
        window.Closed += Closed;
        window.UpdatesAvailable += Notify;
        app.SessionEnding += SessionEnding;
        timer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, async (_, _) => await Tick(DateTimeOffset.UtcNow), window.Dispatcher);
        tray.Visible = true;
    }
    internal void Show()
    {
        if (disposed) return;
        window.Show(); window.ShowInTaskbar = true;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        foreach (Window owned in window.OwnedWindows) if (owned.IsVisible) { owned.Activate(); break; }
    }
    internal void Exit()
    {
        if (window.ApplyingChange) { Show(); return; }
        exiting = true; window.Close();
        // A modal dialog can still cancel closing. Leave a usable tray in that case.
        if (!disposed) exiting = false;
    }
    private void Closing(object? sender, CancelEventArgs e)
    {
        if (e.Cancel || exiting || window.UpdateHandoff || !window.Settings.CloseToTray) return;
        e.Cancel = true; window.Hide(); window.ShowInTaskbar = false;
    }
    private void Closed(object? sender, EventArgs e) { Dispose(); app.Shutdown(); }
    private void SessionEnding(object sender, SessionEndingCancelEventArgs e) => exiting = true;
    internal async Task Tick(DateTimeOffset now)
    {
        if (disposed || !window.Settings.BackgroundUpdates || window.WorkInProgress || now < nextCheck) return;
        nextCheck = now.AddHours(6);
        await window.CheckAllUpdates();
    }
    private void Notify(string message)
    {
        if (!disposed && window.Settings.BackgroundUpdates)
            tray.ShowBalloonTip(8000, "QMM updates available", message, Forms.ToolTipIcon.Info);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        timer.Stop(); window.Closing -= Closing; window.Closed -= Closed; window.UpdatesAvailable -= Notify; app.SessionEnding -= SessionEnding;
        tray.Visible = false; tray.Dispose(); menu.Dispose(); icon.Dispose();
    }
}
