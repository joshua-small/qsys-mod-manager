using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using QdsModManager;
using QdsModManager.App;

internal static class TrayChecks
{
    internal static int Run(string directory)
    {
        string output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
        var checks = new List<string>(); int result = 1;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            TrayLifetime? tray = null; MainWindow? window = null;
            string keyPath = @"Software\QMM.TrayTests\" + Guid.NewGuid().ToString("N");
            try
            {
                string settingsPath = Path.Combine(output, "user-data", "settings.json");
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                File.WriteAllText(settingsPath, "{\"theme\":1}");
                var old = Preferences.Load(settingsPath);
                Check(old.Theme == Appearance.Light && !old.CloseToTray && old.BackgroundUpdates, "Old theme preferences migrate with opt-in close behavior");
                (old with { CloseToTray = true, DesignerPath = @"C:\Remembered Designer" }).Save(settingsPath);
                var dedupe = new UpdateNotifications();
                Check(dedupe.TakeNew(["QMM 9", "Mod 2", "Mod 2"]).Length == 2, "Combined updates deduplicate within a check");
                Check(dedupe.TakeNew([]).Length == 0 && dedupe.TakeNew(["QMM 9"]).Length == 0, "Empty/offline checks do not reset prior notifications");
                Check(dedupe.TakeNew(["Mod 3"]).Length == 1, "A newer mod version is announced");

                var registration = new StartupRegistration(Environment.ProcessPath!, keyPath);
                Check(!registration.Enabled, "Startup defaults off");
                registration.SetEnabled(true);
                using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
                    Check(key!.GetValue(StartupRegistration.ValueName) as string == "\"" + Environment.ProcessPath + "\" --background", "Startup uses a quoted executable path and background argument");
                Check(registration.Enabled, "Startup enabled state reads the registry");
                registration.SetEnabled(false); Check(!registration.Enabled, "Startup can be disabled without elevation");
                using (var key = Registry.CurrentUser.CreateSubKey(keyPath)) key.SetValue(StartupRegistration.ValueName, "\"C:\\Other copy.exe\" --background");
                registration.SetEnabled(false);
                using (var key = Registry.CurrentUser.OpenSubKey(keyPath)) Check(key!.GetValue(StartupRegistration.ValueName) is not null, "Disabling preserves a different copy's startup registration");

                string instanceName = @"Local\QMM.TrayTests." + Guid.NewGuid().ToString("N");
                using var first = new UserInstance(instanceName); using var second = new UserInstance(instanceName);
                Check(first.IsFirst && !second.IsFirst, "Only the first launch owns the UI instance");
                var activation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                // Signal before Listen to cover simultaneous launches.
                second.Activate(); first.Listen(app.Dispatcher, () => activation.TrySetResult());
                await activation.Task.WaitAsync(TimeSpan.FromSeconds(5)); Check(true, "A second launch activates the first, including the startup race");

                var handler = new OfflineHandler(); int managerChecks = 0; bool failManager = false;
                window = new MainWindow(Path.GetDirectoryName(settingsPath), new HttpClient(handler), _ =>
                {
                    managerChecks++;
                    return failManager ? Task.FromException<ManagerRelease>(new IOException("Offline test")) : Task.FromResult(new ManagerRelease("99.0.0", "v99.0.0", "", new string('a', 64), 1));
                });
                window.Left = window.Top = -32000; window.ShowActivated = false; app.MainWindow = window;
                tray = new TrayLifetime(app, window);
                int notices = 0; window.UpdatesAvailable += _ => notices++;
                await window.Initialize(true);
                Check(!window.IsVisible && tray.Visible, "Background startup creates a tray icon without showing the window");
                Check(managerChecks == 1 && handler.Calls == 1 && notices == 1, "Background startup checks manager/catalog and announces a manager update despite offline catalog");
                Check(!window.CanLaunchFromTray, "Launch Designer is unavailable without a selected installation");
                tray.Show(); await Task.Delay(50);
                Check(window.IsVisible && window.ShowInTaskbar, "Open QMM restores the main window and taskbar entry");
                Check(managerChecks == 1, "Showing the window does not repeat initialization");
                var theme = (System.Windows.Controls.ComboBox)typeof(MainWindow).GetField("theme", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                theme.SelectedIndex = 2;
                Check(window.Settings.CloseToTray && window.Settings.DesignerPath == @"C:\Remembered Designer", "Theme changes preserve background preferences and remembered Designer");
                using var download = new CancellationTokenSource();
                var network = typeof(MainWindow).GetField("network", BindingFlags.Instance | BindingFlags.NonPublic)!;
                network.SetValue(window, download);
                window.Close();
                Check(!window.IsVisible && !window.ShowInTaskbar && tray.Visible, "Close-to-tray hides without terminating");
                Check(!download.IsCancellationRequested, "Closing to tray does not cancel an active download"); network.SetValue(window, null);
                await window.CheckAllUpdates();
                Check(notices == 1 && managerChecks == 2, "Repeat background checks do not duplicate notifications");
                failManager = true; await window.CheckAllUpdates();
                Check(notices == 1, "Offline failure does not raise an update notification");
                failManager = false;
                var now = DateTimeOffset.UtcNow.AddHours(7);
                await tray.Tick(now); int afterTimer = managerChecks;
                await tray.Tick(now.AddMinutes(1)); Check(managerChecks == afterTimer, "Timer limits checks to once every six hours");
                window.SaveSettings(window.Settings with { BackgroundUpdates = false });
                await tray.Tick(now.AddHours(7)); Check(managerChecks == afterTimer, "Disabled background preference prevents periodic network checks");
                await window.CheckAllUpdates(); Check(managerChecks == afterTimer + 1 && notices == 1, "Manual checks still work with notifications disabled");
                window.SaveSettings(window.Settings with { BackgroundUpdates = true });
                tray.Show(); window.WindowState = WindowState.Minimized; tray.Show();
                Check(window.WindowState == WindowState.Normal && window.IsVisible, "Tray Open restores a minimized window");
                // Exercise the actual installation-close guard without installing anything.
                var applying = typeof(MainWindow).GetField("applying", BindingFlags.Instance | BindingFlags.NonPublic)!;
                applying.SetValue(window, true); tray.Exit(); window.Close();
                Check(window.IsVisible && tray.Visible, "Exit and Close preserve an active installation");
                applying.SetValue(window, false);
                var dialog = new Window { Owner = window, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
                dialog.Show(); int beforeDialog = managerChecks; await tray.Tick(now.AddHours(8));
                window.OpenUpdateNotification(); Check(window.OwnedWindows.Count == 1, "Notification clicks preserve an existing modal dialog");
                Check(managerChecks == beforeDialog, "A preview/settings dialog defers background checks"); dialog.Close();
                await tray.Tick(now.AddHours(8).AddMinutes(1)); Check(managerChecks == beforeDialog + 1, "Deferred checks resume after the dialog closes");
                window.Close(); tray.Show(); Check(window.IsVisible, "Repeated hide/open preserves a usable window");
                _ = window.Dispatcher.BeginInvoke(() =>
                {
                    var settings = window.OwnedWindows.Cast<Window>().Single(); settings.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)settings.ActualWidth, (int)settings.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(settings); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var image = File.Create(Path.Combine(output, "background-settings.png")); encoder.Save(image); settings.Close();
                });
                window.CreateSettingsDialog().ShowDialog();
                bool closed = false; window.Closed += (_, _) => closed = true;
                tray.Exit(); Check(closed && !tray.Visible, "Explicit Exit closes the hidden-capable window and removes its tray icon");
                File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"Passed {checks.Count} startup, notification and native tray lifecycle checks."); result = 0;
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); Console.Error.WriteLine(error); }
            finally
            {
                tray?.Dispose(); Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
                app.Shutdown();
            }
        };
        app.Run(); return result;
    }
    internal static int Shutdown(string scenario, string directory)
    {
        string output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        bool closed = false, canceled = false; int result = 1; TrayLifetime? tray = null;
        app.Startup += async (_, _) =>
        {
            try
            {
                string settings = Path.Combine(output, "settings.json");
                new Preferences(CloseToTray: scenario != "ordinary", BackgroundUpdates: false).Save(settings);
                var checking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var window = new MainWindow(output, new HttpClient(new OfflineHandler()), async token =>
                {
                    checking.TrySetResult();
                    using var registration = token.Register(() => canceled = true);
                    await Task.Delay(Timeout.Infinite, token);
                    throw new Exception("Unreachable");
                });
                window.Left = window.Top = -32000; window.ShowActivated = false; app.MainWindow = window;
                window.Closed += (_, _) => closed = true;
                tray = new TrayLifetime(app, window);
                await window.Initialize(true);
                if (scenario == "download")
                {
                    _ = window.CheckAllUpdates(); await checking.Task.WaitAsync(TimeSpan.FromSeconds(5)); tray.Exit();
                }
                else if (scenario == "update")
                {
                    typeof(MainWindow).GetField("updateHandoff", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                    app.Shutdown();
                }
                else if (scenario == "session")
                {
                    // Simulate the session-ending event without logging out the CI runner.
                    typeof(TrayLifetime).GetMethod("SessionEnding", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tray, [app, null]);
                    window.Close();
                }
                else if (scenario == "ordinary") window.Close();
                else throw new ArgumentException("Unknown shutdown scenario");
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); app.Shutdown(); }
        };
        app.Exit += (_, _) =>
        {
            // CancellationToken callbacks are synchronous; async catch continuations may still be queued.
            bool passed = closed && tray is { Visible: false } && (scenario != "download" || canceled);
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { scenario, closed, trayRemoved = tray is { Visible: false }, canceled, passed }));
            if (passed) { Console.WriteLine("Passed native shutdown: " + scenario); result = 0; }
            tray?.Dispose();
        };
        app.Run(); return result;
    }
    private sealed class OfflineHandler : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
