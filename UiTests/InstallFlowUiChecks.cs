using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QdsModManager;
using QdsModManager.App;

internal static class InstallFlowUiChecks
{
    internal static int Run(string fixtures, string runningFixture, string directory)
    {
        string output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow? window = null; Process? fakeDesigner = null;
        try
        {
            Catalog.ForTool(Data.Read<ApprovalDocument>(File.ReadAllBytes(Path.Combine(fixtures, "approvals.json"))));
            var packages = Directory.GetFiles(Path.Combine(fixtures, "packages"), "*.qdsmod").Select(ModPackage.Open).ToArray();
            var core = packages.Single(p => p.Manifest.Id == "mods-core");
            var selected = packages.Where(p => p.Manifest.Id is "compact-connectors" or "component-auto-width").ToArray();
            var document = new CatalogDocument(1, packages.Select(p => new CatalogItem(p.Manifest.Id, p.Manifest.Name, "Synthetic UI fixture", Catalog.Mod(p.Manifest.Id).Repository,
                [new(p.Manifest.Version, "10.5", Catalog.HostHash, Catalog.CommonHash, "https://github.com/" + Catalog.Mod(p.Manifest.Id).Repository + "/releases/download/test/" + p.Manifest.Id + ".qdsmod", p.ArchiveHash, new FileInfo(p.Path).Length)])).ToArray());
            string data = Path.Combine(output, "user-data"), cache = Path.Combine(data, "Packages"); Directory.CreateDirectory(cache);
            foreach (var package in selected) File.Copy(package.Path, Path.Combine(cache, package.ArchiveHash + ".qdsmod"));
            var handler = new PackageHandler(packages); using var client = new HttpClient(handler);
            window = new MainWindow(data, client) { Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            window.SmokeSetup("Light", document); window.Show();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            string target = Path.Combine(output, "designer"); Directory.CreateDirectory(target);
            File.WriteAllBytes(Path.Combine(target, "Q-Sys Designer.dll"), [1,2,3]); File.WriteAllBytes(Path.Combine(target, "ModuleCommon.dll"), [4,5,6]);
            window.SelectTarget(target); Pump();
            T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            object? Invoke(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
            var mods = Field<DataGrid>("mods");
            Check(!window.Settings.PreviewChanges, "Preview changes defaults off in a fresh UI");
            window.SaveSettings(window.Settings with { PreviewChanges = true });
            Check(Preferences.Load(Path.Combine(data, "settings.json")).PreviewChanges, "Preview setting persists");
            window.SaveSettings(window.Settings with { PreviewChanges = false });
            Check(!Preferences.Load(Path.Combine(data, "settings.json")).PreviewChanges, "Preview setting can be disabled again");
            foreach (string id in selected.Select(p => p.Manifest.Id))
            {
                var row = mods.Items.Cast<ModRow>().Single(r => r.Id == id); mods.ScrollIntoView(row); Pump();
                var visual = (DataGridRow)mods.ItemContainerGenerator.ContainerFromItem(row); visual.ApplyTemplate(); Pump();
                var box = Descendants(visual).OfType<CheckBox>().Single();
                Check(AutomationProperties.GetName(box) == "Select " + row.Name, "Selection checkbox is labeled for " + row.Name);
                box.IsChecked = true; box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            Check(Field<HashSet<string>>("selectedModIds").Count == 2 && Field<Button>("installSelected").IsEnabled, "Two checked mods enable the batch action");
            window.UpdateLayout();
            var screenshot = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); screenshot.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(screenshot));
            using (var file = File.Create(Path.Combine(output, "batch-selection.png"))) encoder.Save(file);
            Field<TextBox>("search").Text = "Compact"; Pump();
            Check(Field<HashSet<string>>("selectedModIds").Count == 2, "Filtering preserves the checked batch");
            Field<TextBox>("search").Clear(); Pump();
            void Run(Action action, params (string Title, bool Accept, string Text)[] expected)
            {
                int seen = 0; Exception? failure = null; var start = DateTime.UtcNow;
                var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.ApplicationIdle, (_, _) =>
                {
                    foreach (var dialog in window.OwnedWindows.Cast<Window>().ToArray())
                    {
                        try
                        {
                            if (seen >= expected.Length) throw new Exception("Unexpected dialog: " + dialog.Title);
                            var next = expected[seen++];
                            Check(dialog.Title == next.Title, "Dialog appears at the required step: " + next.Title);
                            string text = string.Join("\n", Descendants(dialog).OfType<TextBlock>().Select(t => t.Text));
                            Check(text.Contains(next.Text), "Dialog explains: " + next.Text);
                            dialog.DialogResult = next.Accept;
                        }
                        catch (Exception error) { failure = error; dialog.DialogResult = false; }
                    }
                    if ((DateTime.UtcNow - start).TotalSeconds > 10) failure = new TimeoutException("Installation UI check timed out");
                }, window.Dispatcher);
                try { action(); if (failure is not null) throw failure; Check(seen == expected.Length, "Only the expected dialogs appeared"); }
                finally { timer.Stop(); }
            }
            void Wait(Task task)
            {
                var frame = new DispatcherFrame(); var start = DateTime.UtcNow;
                var poll = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.ApplicationIdle, (_, _) => { if (task.IsCompleted || (DateTime.UtcNow - start).TotalSeconds > 15) frame.Continue = false; }, window.Dispatcher);
                try { if (!task.IsCompleted) Dispatcher.PushFrame(frame); if (!task.IsCompleted) throw new TimeoutException("Installation task did not complete"); task.GetAwaiter().GetResult(); } finally { poll.Stop(); }
            }
            void Install() => Wait((Task)Invoke("InstallSelected")!);
            Run(Install, ("Required dependencies", false, "Q-SYS Mods Core"));
            Check(handler.Requests == 0 && !Directory.Exists(Path.Combine(target, ".qds-mod-manager")), "Canceling dependencies downloads nothing and changes no installation files");
            Run(Install, ("Required dependencies", true, "Q-SYS Mods Core"), ("Untested Designer version", true, DesignerBuild.UntestedWarning));
            Check(handler.Requests == 1 && File.Exists(Path.Combine(cache, core.ArchiveHash + ".qdsmod")), "Accepted dependencies download once and are verified");
            Check(Field<TextBlock>("message").Text.StartsWith("Preview mode:"), "Preview-off install proceeds to execution after necessary prompts");
            var manager = new Manager(target); manager.Apply(manager.PrepareInstall(selected, packages) with { UntestedVersionAcknowledged = true }); window.SelectTarget(target); Pump();
            Run(Install);
            Check(Field<TextBlock>("message").Text.StartsWith("Preview mode:"), "Accepted installation consent avoids repeated prompts when previews are off");
            window.SaveSettings(window.Settings with { PreviewChanges = true });
            Run(Install, ("Preview change", true, "Install 2 selected mods"));
            window.SaveSettings(window.Settings with { PreviewChanges = false });
            Field<Button>("clearSelection").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(Field<HashSet<string>>("selectedModIds").Count == 0 && !Field<Button>("installSelected").IsEnabled, "Clear selection disables the batch action");
            string other = Path.Combine(output, "conflict"); Directory.CreateDirectory(other);
            File.WriteAllBytes(Path.Combine(other, "Q-Sys Designer.dll"), [1,2,3]); File.WriteAllBytes(Path.Combine(other, "ModuleCommon.dll"), [4,5,6]);
            var conflictManager = new Manager(other); var geometry = packages.Single(p => p.Manifest.Id == "automatic-geometry");
            conflictManager.Apply(conflictManager.Prepare("Install", geometry.Manifest.Id, geometry.Path) with { UntestedVersionAcknowledged = true });
            window.SelectTarget(other); Pump();
            var choices = selected.Select(p => new VersionChoice(p.Manifest.Version, p.Manifest.Version, p, null, null)).ToArray();
            Run(() => Wait((Task)Invoke("InstallChoices", (object)choices)!), ("Could not complete change", true, "Disable Automatic Geometry"));
            Check(conflictManager.Inspect(packages).Profile!.Mods.Length == 1, "Conflict popup leaves the existing installation unchanged");
            window.SelectTarget(target); Pump();
            foreach (string path in Directory.GetFiles(runningFixture)) File.Copy(path, Path.Combine(target, Path.GetFileName(path)));
            File.Copy(Path.Combine(target, "RunningDesigner.exe"), Path.Combine(target, "Q-Sys Designer.exe"));
            string ready = Path.Combine(output, "running.ready");
            var info = new ProcessStartInfo(Path.Combine(target, "Q-Sys Designer.exe")) { UseShellExecute = false, CreateNoWindow = true }; info.ArgumentList.Add(ready);
            fakeDesigner = Process.Start(info)!;
            Check(SpinWait.SpinUntil(() => File.Exists(ready) || fakeDesigner.HasExited, TimeSpan.FromSeconds(10)) && File.Exists(ready), "Test-owned Designer process starts");
            bool result = true;
            Run(() => result = (bool)Invoke("RequireDesignerClosed", target)!, ("Designer is still open", false, "Save your open designs"));
            Check(!result && !fakeDesigner.HasExited, "Cancel keeps Designer running and cancels the change");
            // Retry must inspect again: close only the test-owned process when the first prompt opens.
            var stop = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.ApplicationIdle, (_, _) => { if (window.OwnedWindows.Count > 0 && !fakeDesigner.HasExited) { fakeDesigner.Kill(); fakeDesigner.WaitForExit(); } }, window.Dispatcher);
            try { Run(() => result = (bool)Invoke("RequireDesignerClosed", target)!, ("Designer is still open", true, "QMM will not close Designer automatically")); }
            finally { stop.Stop(); }
            Check(result, "Retry continues only after the selected Designer process exits");
            File.WriteAllBytes(Path.Combine(output, "checks.json"), Data.Bytes(checks)); Console.WriteLine($"Passed {checks.Count} native WPF installation flow checks."); return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); Console.Error.WriteLine(error); return 1; }
        finally { if (fakeDesigner is not null) { if (!fakeDesigner.HasExited) fakeDesigner.Kill(); fakeDesigner.Dispose(); } window?.Close(); app.Shutdown(); }
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private sealed class PackageHandler(ModPackage[] packages) : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++; string id = Path.GetFileNameWithoutExtension(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(packages.Single(p => p.Manifest.Id == id).Path)) });
        }
    }
}
