using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace QdsModManager.App;

internal sealed partial class MainWindow
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly UpdateNotifications notifications = new();
    private bool initialized, checkingAll, closed, catalogFresh;
    private Task? initialization;
    private int dialogDepth;
    internal Preferences Settings => themes.Settings;
    internal void SaveSettings(Preferences settings) => themes.Save(settings);
    internal bool ApplyingChange => applying;
    internal bool UpdateHandoff => updateHandoff;
    internal bool WorkInProgress => busy || applying || updateHandoff || updateChecking || checkingAll || dialogDepth > 0 || !initialized || OwnedWindows.Cast<Window>().Any(w => w.IsVisible);
    internal bool CanLaunchFromTray => !WorkInProgress && inspection is { Pending: null } && File.Exists(Path.Combine(target.Text, "Q-Sys Designer.exe"));
    internal event Action<string>? UpdatesAvailable;
    internal event Action? UpdateStateChanged;
    internal void ReportShellError(Exception error) => errors.Add("Windows update shortcuts: " + error.GetBaseException().Message);
    internal UpdateStatus UpdateState => new(managerUpdate?.Version, AvailableUpdates().Length, !WorkInProgress && !closed,
        !WorkInProgress && !closed && inspection is { Pending: null });

    internal Task Initialize(bool background) => initialization ??= InitializeCore(background);
    private async Task InitializeCore(bool background)
    {
        if (smoke || initialized || closed) return; initialized = true;
        MigrateKnownInstallations();
        if (target.Items.Count == 0)
        {
            foreach (var path in Manager.Discover()) target.Items.Add(path);
            string? saved = Settings.DesignerPath;
            if (saved is not null && Directory.Exists(saved)) { if (!target.Items.Contains(saved)) target.Items.Add(saved); target.SelectedItem = saved; }
            else if (target.Items.Count > 0) target.SelectedIndex = 0;
        }
        Refresh();
        if (!background || Settings.BackgroundUpdates) await CheckAllUpdates();
        var notice = AppUpdate.ConsumeNotice(); if (notice is not null) message.Text = notice;
    }
    internal async Task CheckAllUpdates()
    {
        if (closed || busy || updateChecking || checkingAll) return;
        checkingAll = true;
        try
        {
            await FetchCatalog(IsVisible);
            if (closed) return;
            await CheckManagerUpdate();
            if (closed || !Settings.BackgroundUpdates) return;
            var updates = new List<string>();
            if (managerUpdate is not null) updates.Add("QMM " + managerUpdate.Version);
            // Only announce a fresh, verified catalog for the selected Designer installation.
            if (catalogFresh)
                updates.AddRange(AvailableUpdates().Select(u => u.Item.Name + " " + u.Release.Version));
            string[] fresh = notifications.TakeNew(updates);
            if (fresh.Length > 0) UpdatesAvailable?.Invoke(string.Join(", ", fresh.Take(5)) + (fresh.Length > 5 ? " and more" : "") + ". Open QMM to review.");
        }
        finally { checkingAll = false; Buttons(); }
    }
    internal bool LaunchFromTray()
    {
        if (!CanLaunchFromTray) return false;
        message.Text = ""; Launch(); return string.IsNullOrEmpty(message.Text);
    }
    internal void OpenUpdateNotification()
    {
        if (WorkInProgress) return;
        if (managerUpdate is not null) ShowManagerUpdateAction(); else ReviewUpdates();
    }
    internal void ReviewCheckedUpdates()
    {
        if (WorkInProgress || closed) return;
        if (AvailableUpdates().Length > 0) ReviewUpdates();
        else if (managerUpdate is not null) ShowManagerUpdateAction();
        else message.Text = "Update check complete. See Mod updates and Manager updates in the sidebar.";
    }
    private void ShowManagerUpdateAction()
    {
        installUpdate.BringIntoView(); installUpdate.Focus();
        message.Text = "QMM " + managerUpdate!.Version + " is available. Choose Install in Manager updates when you are ready.";
    }
    internal async Task RunUpdateCommand(UpdateCommand command)
    {
        // Shell tasks never carry a package, version, path or arbitrary payload.
        // Re-read availability, then use the sidebar's explicit install workflow.
        if (closed || WorkInProgress) return;
        if (command == UpdateCommand.Manager && managerUpdate is not null) await InstallManagerUpdate();
        else if (command == UpdateCommand.Mods && inspection is { Pending: null } && AvailableUpdates().Length > 0) ReviewUpdates();
        else message.Text = "This update action is no longer available. Check for updates in the sidebar.";
    }
    private T RunDialog<T>(Func<T> show)
    {
        // Include native file pickers as well as WPF dialogs in shell guards.
        dialogDepth++; UpdateStateChanged?.Invoke();
        try { return show(); }
        finally { dialogDepth--; UpdateStateChanged?.Invoke(); }
    }

    private void AddBackgroundSettings(Panel panel)
    {
        panel.Children.Add(SectionHeading("Startup & background", 0));
        var startup = new StartupRegistration(Environment.ProcessPath!);
        var start = new CheckBox { Content = "Start with Windows", Margin = new Thickness(0, 4, 0, 8), IsEnabled = !smoke && AppIdentity.IsExecutable(Environment.ProcessPath, AppContext.BaseDirectory) };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap };
        try { start.IsChecked = startup.Enabled; }
        catch (Exception error) { start.IsEnabled = false; feedback.Text = "Could not read startup setting: " + error.Message; }
        start.Click += (_, _) =>
        {
            try { startup.SetEnabled(start.IsChecked is true); feedback.Text = "Startup preference saved. Windows Startup apps can also disable it."; }
            catch (Exception error) { start.IsChecked = start.IsChecked is not true; feedback.Text = "Could not save startup setting: " + error.Message; }
        };
        panel.Children.Add(start);
        Add("Close window to system tray", Settings.CloseToTray, value => Settings with { CloseToTray = value });
        Add("Check for updates in the background", Settings.BackgroundUpdates, value => Settings with { BackgroundUpdates = value });
        panel.Children.Add(Muted("Startup opens quietly after sign-in. Background checks run every six hours for QMM and the selected Designer installation. Updates are installed only when you choose them. Use the tray menu to exit.", new Thickness(0, 4, 0, 8)));
        panel.Children.Add(feedback);
        void Add(string label, bool enabled, Func<bool, Preferences> change)
        {
            var box = new CheckBox { Content = label, IsChecked = enabled, Margin = new Thickness(0, 4, 0, 8), IsEnabled = !smoke };
            box.Click += (_, _) =>
            {
                try { SaveSettings(change(box.IsChecked is true)); feedback.Text = "Preference saved."; }
                catch (Exception error) { box.IsChecked = box.IsChecked is not true; feedback.Text = "Could not save preference: " + error.Message; }
            };
            panel.Children.Add(box);
        }
    }
}
