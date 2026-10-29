using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using QdsModManager;
using QdsModManager.App;
using Forms = System.Windows.Forms;

internal static class UpdateChecks
{
    internal static int Run(string directory)
    {
        string output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
        var checks = new List<string>(); int result = 1;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            MainWindow? window = null; TrayLifetime? tray = null; TaskbarUpdates? taskbar = null;
            try
            {
                using var client = new HttpClient(new RejectNetwork());
                window = new MainWindow(Path.Combine(output, "user-data"), client, _ => throw new Exception("Unexpected update request"));
                window.SmokeSetup("Light", null); Set("initialized", true);
                window.SaveSettings(window.Settings with { PreviewChanges = true });
                window.Left = window.Top = -32000; window.ShowActivated = false; window.ShowInTaskbar = false; window.Show(); Pump();
                IReadOnlyList<JumpTask> published = []; int publications = 0;
                // Exercise task construction and overlays without writing Explorer's Jump List.
                taskbar = new TaskbarUpdates(window, tasks => { published = tasks; publications++; });
                tray = new TrayLifetime(app, window);
                var trayIcon = Field<Forms.NotifyIcon>(tray, "tray");
                var originalIcon = trayIcon.Icon;
                var managerItem = Field<Forms.ToolStripItem>(tray, "managerInstall");
                var modItem = Field<Forms.ToolStripItem>(tray, "modInstall");
                Check(window.TaskbarItemInfo.Overlay is null && published.Count == 0 && !managerItem.Available && !modItem.Available, "No updates: no badge or install tasks");

                var newer = new ManagerRelease("99.0.0", "v99.0.0", "", new string('a', 64), 1);
                Call("ShowManagerRelease", newer); Call("Buttons"); Pump();
                Check(window.TaskbarItemInfo.Overlay is not null && trayIcon.Icon != originalIcon, "Manager update badges both icons");
                using (var icon = trayIcon.Icon!.ToBitmap()) icon.Save(Path.Combine(output, "tray-update-icon.png"), System.Drawing.Imaging.ImageFormat.Png);
                Check(published.Count == 1 && published[0].Title == "Install Manager 99.0.0" && published[0].Arguments == "--install-manager-update", "Taskbar Manager action uses exact version and fixed command");
                Check(managerItem.Available && managerItem.Enabled && managerItem.Text == "Install Manager 99.0.0" && !modItem.Available, "Tray shows only the available Manager action");
                int stable = publications; Call("Buttons"); Check(publications == stable, "Unchanged state does not rewrite the Jump List");

                var definitions = Catalog.Mods.Take(2).ToArray();
                var profile = ModProfile.Create("Update fixture") with { Mods = definitions.Select(m => new ProfileMod(m.Id, "1.0.0", new string('b', 64), true)).ToArray() };
                var inspection = new Inspection(Path.Combine(output, "synthetic-designer"), false, "fixture", [], null, false, profile) { Build = new DesignerBuild(Catalog.HostHash, Catalog.CommonHash, true) };
                var catalog = new CatalogDocument(1, definitions.Select(m => new CatalogItem(m.Id, m.Name, m.Description, m.Repository,
                    [new("2.0.0", "10.5", Catalog.HostHash, Catalog.CommonHash, "", new string('c', 64), 1)])).ToArray());
                Set("hostHash", Catalog.HostHash); Set("commonHash", Catalog.CommonHash); Set("inspection", inspection); Set("snapshot", new CatalogSnapshot(DateTimeOffset.UtcNow, catalog));
                Call("Filter"); Call("Buttons"); Pump();
                Check(published.Select(t => t.Title).SequenceEqual(["Install Manager 99.0.0", "Install 2 mod updates"]), "Both update categories appear in the taskbar menu: " + JsonSerializer.Serialize(window.UpdateState) + " / " + string.Join(", ", published.Select(t => t.Title)));
                Check(modItem.Available && modItem.Enabled && modItem.Text == "Install 2 mod updates", "Tray uses the same mod count");
                Check(Get<Button>("updates").Content as string == "Install 2 mod updates" && Get<Button>("installUpdate").Content as string == "Install 99.0.0", "Sidebar install labels match the request");
                Call("ShowManagerRelease", new ManagerRelease(AppUpdate.CurrentVersion, "current", "", new string('a', 64), 1)); Call("Buttons");
                Check(window.TaskbarItemInfo.Overlay is not null && trayIcon.Icon != originalIcon && published.Count == 1 && !managerItem.Available && modItem.Available, "Mod-only updates keep both badges and only the mod action");
                Call("ShowManagerRelease", newer); Call("Buttons");

                foreach (string appearance in new[] { "Light", "Dark", "System" })
                {
                    window.SmokeSetup(appearance, null); Call("Buttons"); Pump();
                    foreach (string button in new[] { "updates", "installUpdate" })
                    {
                        var control = Get<Button>(button); control.ApplyTemplate();
                        var surface = (Border)control.Template.FindName("Surface", control);
                        Check(((SolidColorBrush)surface.Background).Color == Color.FromRgb(0, 115, 230) && ((SolidColorBrush)control.Foreground).Color == Colors.White, appearance + ": blue and white " + button);
                        Check(control.ContentTemplate is not null && AutomationProperties.GetName(control).StartsWith("Install"), appearance + ": download icon and accessible name for " + button);
                    }
                    Check(IsBelow(Get<Button>("installUpdate"), Get<Button>("updates")), appearance + ": Manager updates follow Mod updates");
                    var theme = Get<ComboBox>("theme"); var launch = Get<Button>("launch");
                    Check(IsBelow(launch, theme) && theme.IsVisible, appearance + ": Appearance stays in sidebar above Launch");
                    var tools = Descendants(window).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "More tools");
                    Check(tools.IsVisible && tools.ContextMenu.Items.Count == 4, appearance + ": persistent Tools menu retains import, undo, migration and help");
                    var undoCommand = (MenuItem)tools.ContextMenu.Items[1]; Check(!undoCommand.IsEnabled, appearance + ": Tools follows unavailable undo state");
                    Check(Get<ScrollViewer>("sidebarScroll").ScrollableHeight < 1, appearance + ": normal sidebar fits both update sections");
                    Capture((FrameworkElement)window.Content, Path.Combine(output, appearance + "-sidebar.png"));
                    var dialog = window.CreateSettingsDialog(); dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = dialog.Top = -32000; dialog.ShowInTaskbar = false; dialog.ShowActivated = false; dialog.Show(); Pump();
                    var scroll = (ScrollViewer)dialog.Content;
                    Check(scroll.ScrollableHeight < 1 && dialog.ActualHeight <= dialog.MaxHeight, appearance + ": Settings fits its content without default scrolling");
                    Check(dialog.FontSize == window.FontSize && dialog.ActualHeight < 640, appearance + ": Settings matches main font size and fits a 768px display (height " + dialog.ActualHeight + ")");
                    Check(!Descendants(dialog).OfType<ComboBox>().Any() && !Descendants(dialog).Contains(Get<Button>("installUpdate")), appearance + ": Appearance and Manager updates removed from Settings");
                    Capture((FrameworkElement)dialog.Content, Path.Combine(output, appearance + "-settings.png"));
                    dialog.Close(); Pump();
                }
                foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                {
                    window.SmokeScale(scale); window.Width = 1366; window.Height = 740; Pump();
                    Check(Get<ComboBox>("theme").ActualWidth > 100 && Get<Button>("launch").ActualHeight >= 32, "Sidebar controls remain sized at scale " + scale);
                    var dialog = window.CreateSettingsDialog(); ((FrameworkElement)dialog.Content).LayoutTransform = new ScaleTransform(scale, scale);
                    dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = dialog.Top = -32000; dialog.ShowActivated = false; dialog.ShowInTaskbar = false; dialog.Show(); Pump();
                    Check(dialog.ActualHeight <= dialog.MaxHeight + 1 && ((ScrollViewer)dialog.Content).HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled, "Settings respects screen height at scale " + scale);
                    dialog.Close();
                }
                window.SmokeScale(1); window.Width = window.MinWidth; window.Height = window.MinHeight; Pump();
                Check(Get<ComboBox>("theme").IsVisible && Get<ScrollViewer>("sidebarScroll").ViewportHeight > 0, "Minimum window preserves Appearance and scrollable sidebar");
                window.Width = 1280; window.Height = 740;

                foreach (string flag in new[] { "busy", "applying", "updateChecking", "checkingAll", "updateHandoff" })
                {
                    Set(flag, true); Call("Buttons");
                    Check(window.TaskbarItemInfo.Overlay is not null && published.Count == 0 && !managerItem.Enabled && !modItem.Enabled, flag + ": badges remain while update actions are unavailable");
                    await window.RunUpdateCommand(UpdateCommand.Manager); await window.RunUpdateCommand(UpdateCommand.Mods);
                    Check(window.OwnedWindows.Count == 0, flag + ": shell actions cannot open an update flow");
                    Set(flag, false); Call("Buttons");
                }
                Set("inspection", inspection with { Pending = "recovery required" }); Call("Buttons");
                Check(!modItem.Enabled && published.All(t => t.Arguments != "--install-mod-updates"), "Interrupted mod operation blocks both shell mod actions");
                await window.RunUpdateCommand(UpdateCommand.Mods); Check(window.OwnedWindows.Count == 0, "Recovery guard rejects a stale mod task");
                Set("inspection", inspection);
                var owned = new Window { Owner = window, ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 }; owned.Show();
                await window.RunUpdateCommand(UpdateCommand.Manager); await window.RunUpdateCommand(UpdateCommand.Mods);
                Check(window.OwnedWindows.Count == 1, "Existing dialog prevents overlapping shell update flows"); owned.Close();

                CancelNext("Preview change"); await window.RunUpdateCommand(UpdateCommand.Manager);
                Check(!window.WorkInProgress && Get<ManagerRelease>("managerUpdate") == newer, "Canceled Manager preview keeps availability without staging an update");
                Check(published.Count == 2, "Closing a preview restores the taskbar actions");
                window.SaveSettings(window.Settings with { PreviewChanges = false });
                window.OpenUpdateNotification();
                Check(Get<TextBlock>("message").Text.Contains("Choose Install in Manager updates") && window.OwnedWindows.Count == 0, "Notification click only reveals Manager install action when previews are off");
                Set("inspection", inspection with { Profile = ModProfile.Create("Empty installation") });
                window.ReviewCheckedUpdates();
                Check(Get<TextBlock>("message").Text.Contains("Choose Install in Manager updates") && window.OwnedWindows.Count == 0, "Checking for Manager updates cannot start installation when previews are off");
                Set("inspection", inspection); Call("Buttons");
                await window.RunUpdateCommand(UpdateCommand.Manager);
                Check(Get<TextBlock>("message").Text == "Preview mode: no Manager update is downloaded or installed." && !window.WorkInProgress, "Preview harness blocks Manager staging even when change previews are off");
                window.SaveSettings(window.Settings with { PreviewChanges = true });
                _ = window.Dispatcher.BeginInvoke(() => window.OwnedWindows.Cast<Window>().Single().DialogResult = true);
                await window.RunUpdateCommand(UpdateCommand.Manager);
                Check(Get<TextBlock>("message").Text == "Preview mode: no Manager update is downloaded or installed." && !window.WorkInProgress, "Confirmed preview harness cannot stage a Manager update");
                CancelNext("Mod updates"); await window.RunUpdateCommand(UpdateCommand.Mods);
                Check(Get<Inspection>("inspection").Profile == profile, "Canceled mod review leaves profile pins intact");
                Set("inspection", inspection with { Profile = ModProfile.Create("Empty installation") }); Call("Buttons");
                Check(!modItem.Available && published.Count == 1, "Switching to an empty installation removes mod tasks");
                Set("inspection", inspection with { Profile = profile with { Mods = [profile.Mods[0]] } }); Call("Buttons");
                Check(modItem.Text == "Install 1 mod update", "Single mod update uses singular label");
                Set("hostHash", new string('d', 64)); Set("inspection", inspection with { Build = new DesignerBuild(new string('d', 64), Catalog.CommonHash, false) }); Call("Buttons");
                Check(modItem.Available && window.UpdateState.ModCount == 2, "Untested Designer keeps update routes available for guarded exact-build opt-in");
                Check(Get<TextBlock>("compatibility").Visibility == Visibility.Visible, "Untested installation keeps its warning area visible");
                var unavailable = catalog with { Mods = catalog.Mods.Select(m => m with { Releases = m.Releases.Select(r => r with { HostSha256 = new string('e', 64) }).ToArray() }).ToArray() };
                Set("snapshot", new CatalogSnapshot(DateTimeOffset.UtcNow, unavailable)); Call("Buttons");
                Check(!modItem.Available && window.UpdateState.ModCount == 0, "Packages outside the supported target contract do not produce shell updates");
                Set("snapshot", new CatalogSnapshot(DateTimeOffset.UtcNow, catalog)); Set("hostHash", Catalog.HostHash);
                Set("inspection", inspection with { Profile = profile with { Mods = profile.Mods.Select(p => p with { Version = "2.0.0" }).ToArray() } });
                Call("ShowManagerRelease", new ManagerRelease(AppUpdate.CurrentVersion, "current", "", new string('a', 64), 1)); Call("Buttons");
                Check(window.TaskbarItemInfo.Overlay is null && trayIcon.Icon == originalIcon && published.Count == 0, "Up-to-date Manager and mods clear both badges and menu actions");
                await window.RunUpdateCommand(UpdateCommand.Manager); await window.RunUpdateCommand(UpdateCommand.Mods);
                Check(window.OwnedWindows.Count == 0, "Stale update commands do not launch installers");

                foreach (var command in Enum.GetValues<UpdateCommand>())
                {
                    string argument = UpdateStatus.Argument(command);
                    Check(UpdateStatus.Parse(argument.Length == 0 ? [] : [argument]) == command, "Parse shell command " + command);
                }
                Check(UpdateStatus.Parse(["--install-manager-update", "untrusted-path"]) is null && UpdateStatus.Parse(["--unknown"]) is null, "Shell parser rejects extra payloads and unknown commands");
                Check(ShellPreview.Arguments(@"C:\Preview with spaces", "--install-mod-updates") == "--shell-preview \"C:\\Preview with spaces\" --install-mod-updates", "Explorer preview task retains the isolated folder and fixed command");
                string channel = @"Local\QMM.UpdateTests." + Guid.NewGuid().ToString("N");
                using var first = new UserInstance(channel); using var second = new UserInstance(channel);
                // Use the same randomly scoped channel for all signals; no running app is contacted.
                var received = new List<UpdateCommand>(); var completion = new TaskCompletionSource();
                second.Activate(UpdateCommand.Mods);
                first.Listen(app.Dispatcher, command => { received.Add(command); if (received.Count == 3) completion.TrySetResult(); });
                second.Activate(UpdateCommand.Manager); second.Activate();
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(received.Order().SequenceEqual(Enum.GetValues<UpdateCommand>()), "Single-instance routing preserves each command including a startup signal");
                taskbar.Dispose(); taskbar = null;
                Check(published.Count == 0 && window.TaskbarItemInfo.Overlay is null, "Exit clears taskbar tasks and overlay");
                File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"Passed {checks.Count} update surface, routing and layout checks."); result = 0;

                void Set(string name, object? value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
                T Get<T>(string name) => Field<T>(window, name);
                void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
                void CancelNext(string title) => window.Dispatcher.BeginInvoke(() =>
                {
                    var dialog = window.OwnedWindows.Cast<Window>().Single(); Check(dialog.Title == title, "Shell command opens existing " + title + " workflow");
                    Check(published.Count == 0 && !managerItem.Enabled && !modItem.Enabled && window.TaskbarItemInfo.Overlay is not null, "Modal " + title + " temporarily blocks shell actions while retaining the badge"); dialog.Close();
                });
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); Console.Error.WriteLine(error); }
            finally { taskbar?.Dispose(); tray?.Dispose(); window?.Close(); app.Shutdown(); }
        };
        app.Run(); return result;
    }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static bool IsBelow(FrameworkElement lower, FrameworkElement upper) => lower.TranslatePoint(new Point(), Application.Current.MainWindow).Y > upper.TranslatePoint(new Point(), Application.Current.MainWindow).Y;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static void Capture(FrameworkElement visual, string path)
    {
        visual.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new Exception("Unexpected network access: " + request.RequestUri);
    }
}
