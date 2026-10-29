using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using QdsModManager;
using QdsModManager.App;

// Interactive Explorer acceptance fixture. This runs the product window and shell
// adapters with synthetic availability, separate identity/data, and no network.
internal static class ShellPreview
{
    internal static string Arguments(string directory, string command) => "--shell-preview \"" + directory + "\"" + (command.Length == 0 ? "" : " " + command);
    internal static int Run(string directory, string[] arguments, bool headless = false)
    {
        var command = UpdateStatus.Parse(arguments);
        if (command is null || !Path.IsPathFullyQualified(directory)) throw new ArgumentException("Use an absolute, isolated output folder and an optional fixed update command.");
        string output = Path.GetFullPath(directory), marker = Path.Combine(output, "shell-preview.json");
        const string identity = "QMM synthetic shell preview v1";
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any() && (!File.Exists(marker) || File.ReadAllText(marker) != JsonSerializer.Serialize(identity)))
            throw new InvalidOperationException("Use a new empty folder. Existing application data cannot be used for this preview.");
        Directory.CreateDirectory(output); File.WriteAllText(marker, JsonSerializer.Serialize(identity));
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output.ToUpperInvariant() + WindowsIdentity.GetCurrent().User!.Value)))[..24];
        using var instance = new UserInstance(@"Local\QMM.ShellPreview." + suffix);
        if (!instance.IsFirst) { instance.Activate(command.Value); return 0; }
        if (!headless) Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID("QMM.ShellPreview." + suffix));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int result = 0;
        app.DispatcherUnhandledException += (_, e) => { Log("error", e.Exception.ToString()); result = 1; e.Handled = true; app.Shutdown(); };
        using var client = new HttpClient(new RejectNetwork());
        MainWindow? window = null; TrayLifetime? tray = null; TaskbarUpdates? taskbar = null; Window? controls = null;
        app.Startup += async (_, _) =>
        {
            window = new MainWindow(Path.Combine(output, "user-data"), client, _ => Task.FromResult(Release(true)));
            window.SmokeSetup("Light", null); Set("initialized", true);
            window.SaveSettings(window.Settings with { PreviewChanges = true, CloseToTray = true, BackgroundUpdates = false });
            window.Title += " · Isolated preview";
            if (headless) { window.Left = window.Top = -32000; window.ShowInTaskbar = false; window.ShowActivated = false; }
            var definitions = Catalog.Mods.Take(2).ToArray();
            var profile = ModProfile.Create("Synthetic update preview") with { Mods = definitions.Select(m => new ProfileMod(m.Id, "1.0.0", new string('b', 64), true)).ToArray() };
            var inspection = new Inspection(Path.Combine(output, "synthetic-designer"), false, "fixture", [], null, false, profile) { Build = new DesignerBuild(Catalog.HostHash, Catalog.CommonHash, true) };
            var catalog = new CatalogDocument(1, definitions.Select(m => new CatalogItem(m.Id, m.Name, "Synthetic update preview", m.Repository,
                [new("2.0.0", "10.5", Catalog.HostHash, Catalog.CommonHash, "https://example.invalid/blocked.qdsmod", new string('c', 64), 1)])).ToArray());
            Set("snapshot", new CatalogSnapshot(DateTimeOffset.UtcNow, catalog));
            var target = Field<ComboBox>(window, "target"); target.Items.Add(inspection.Target); target.SelectedItem = inspection.Target;
            SetState("Both");
            window.Show();
            taskbar = new TaskbarUpdates(window, tasks =>
            {
                if (headless) { Log("substituted-jump-list", tasks.Select(t => t.Title).ToArray()); return; }
                var list = new JumpList(tasks.Select(t => new JumpTask
                {
                    Title = t.Title, Description = t.Description, ApplicationPath = Environment.ProcessPath!,
                    WorkingDirectory = AppContext.BaseDirectory, IconResourcePath = Environment.ProcessPath!, IconResourceIndex = 0,
                    Arguments = Arguments(output, t.Arguments)
                }), false, false);
                list.JumpItemsRejected += (_, e) => Log("jump-items-rejected", e.RejectionReasons.Select(r => r.ToString()).ToArray());
                JumpList.SetJumpList(app, list); list.Apply(); Log("jump-list", tasks.Select(t => t.Title).ToArray());
            });
            tray = new TrayLifetime(app, window);
            instance.Listen(app.Dispatcher, async action => { Log("shell-command", action.ToString()); await tray.RunCommand(action); });
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock { Text = "Synthetic availability. No downloads, installation or Designer launch.\nUse this panel to change states, then inspect the real taskbar/tray.\nExit using QMM's tray menu; closing QMM hides it to the tray.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            foreach (string state in new[] { "Both", "Manager only", "Mods only", "None", "Busy", "Recovery", "Untested" })
            {
                var button = new Button { Content = state, Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(8, 4, 8, 4) };
                button.Click += (_, _) => SetState(state); panel.Children.Add(button);
            }
            controls = new Window { Title = "QMM preview controls", Width = 390, SizeToContent = SizeToContent.Height, Content = panel, ShowInTaskbar = false };
            if (!headless) { controls.Show(); window.Activate(); }
            window.Closed += (_, _) => controls.Close();
            Log("ready", new { appId = "QMM.ShellPreview." + suffix, productVersion = AppUpdate.CurrentVersion, productSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))).ToLowerInvariant() });
            if (headless)
            {
                foreach (string state in new[] { "Manager only", "Mods only", "None", "Busy", "Recovery", "Untested", "Both" })
                {
                    SetState(state); var actual = window.UpdateState;
                    if (actual.ModCount != (state is "None" or "Manager only" ? 0 : 2) || actual.HasUpdates != (state != "None")) throw new Exception("Unexpected preview availability: " + state);
                    if (state == "Busy" && (actual.CanUpdateManager || actual.CanUpdateMods) || state == "Recovery" && actual.CanUpdateMods) throw new Exception("Preview guard failed: " + state);
                }
                Log("fixture-check", "Seven states passed; real Jump List and input not exercised.");
                tray.Exit();
            }
            else await tray.RunCommand(command.Value);

            void SetState(string state)
            {
                if (window.OwnedWindows.Cast<Window>().Any(w => w.IsVisible)) { Log("state-blocked-by-dialog", state); return; }
                Set("busy", state == "Busy");
                bool untested = state == "Untested";
                Set("hostHash", untested ? new string('d', 64) : Catalog.HostHash); Set("commonHash", Catalog.CommonHash);
                var current = inspection with
                {
                    Pending = state == "Recovery" ? "synthetic-recovery" : null,
                    Build = untested ? new DesignerBuild(new string('d', 64), Catalog.CommonHash, false) : inspection.Build,
                    Profile = state is "None" or "Manager only" ? ModProfile.Create("Synthetic empty profile") : profile
                };
                Set("inspection", current);
                var profiles = Field<ComboBox>(window, "profiles"); profiles.ItemsSource = new[] { current.Profile }; profiles.SelectedIndex = 0;
                Field<TextBlock>(window, "compatibility").Text = untested ? DesignerBuild.UntestedWarning : state == "Recovery" ? "Synthetic interrupted operation needs recovery." : "";
                Call("ShowManagerRelease", Release(state is not ("None" or "Mods only"))); Call("Filter"); Call("Buttons");
                Field<TextBlock>(window, "message").Text = "Isolated preview · " + state + ". No changes are installed.";
                Log("state", new { state, availability = window.UpdateState });
            }
            void Set(string name, object? value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
            void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        };
        try { app.Run(); return result; }
        finally { taskbar?.Dispose(); tray?.Dispose(); Log("exit", "Preview closed"); }
        void Log(string kind, object detail) => File.AppendAllText(Path.Combine(output, "shell-events.jsonl"), JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, kind, detail }) + "\n");
    }
    private static ManagerRelease Release(bool newer) => new(newer ? "99.0.0" : AppUpdate.CurrentVersion, "synthetic", "", new string('a', 64), 1);
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new InvalidOperationException("Isolated preview blocks every network request.");
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
