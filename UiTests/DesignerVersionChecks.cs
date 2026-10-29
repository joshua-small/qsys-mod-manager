using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QdsModManager;
using QdsModManager.App;

internal static class DesignerVersionChecks
{
    internal static int Run(string versionZero, string versionOne, string directory)
    {
        string output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool passed, string label) { if (!passed) throw new Exception(label); checks.Add(label); }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; MainWindow? window = null;
        try
        {
            string target = Path.Combine(output, "Q-SYS Designer 10.5"); Directory.CreateDirectory(target);
            string host = Path.Combine(target, "Q-Sys Designer.dll"), executable = Path.Combine(target, "Q-Sys Designer.exe");
            File.Copy(versionZero, host); File.Copy(versionZero, executable);
            File.WriteAllBytes(Path.Combine(target, "ModuleCommon.dll"), [4, 5, 6]);
            Check(DesignerInstallation.Read(target).Label == "Q-SYS Designer 10.5.0", "Show the zero patch component from binary metadata");
            window = new MainWindow(Path.Combine(output, "user-data")) { Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            window.SmokeSetup("Dark", Catalog.Current); window.Show(); window.SelectTarget(target); Pump();
            var selector = Field<ComboBox>("target");
            Check(Descendants(selector).OfType<TextBlock>().Any(t => t.Text == "Q-SYS Designer 10.5.0"), "Selected dropdown renders full detected version");
            File.Copy(versionOne, host, true); window.SelectTarget(target); Pump();
            Check(DesignerInstallation.Read(target).Label == "Q-SYS Designer 10.5.1", "Host DLL version takes precedence over older executable and unchanged folder name");
            Check(Descendants(selector).OfType<TextBlock>().Any(t => t.Text == "Q-SYS Designer 10.5.1"), "Refresh updates the selected label after an in-place patch: " + string.Join(" | ", Descendants(selector).OfType<TextBlock>().Select(t => t.Text)));
            Check((string)selector.ToolTip == target && selector.Text == target, "Version label preserves the real installation path and tooltip");
            var warning = Field<TextBlock>("compatibility");
            Check(warning.Text == "Q-SYS Designer 10.5.1\n" + DesignerBuild.UntestedWarning, "Sidebar warning identifies 10.5.1 and retains required warning text");
            Check(Field<Inspection>("inspection").Build?.Tested is false, "Plausible version metadata cannot certify synthetic host hashes");
            var mods = Field<DataGrid>("mods"); mods.SelectedIndex = 0; Pump();
            Check(Field<TextBlock>("availability").Text.Contains("Q-SYS Designer 10.5.1\n" + DesignerBuild.UntestedWarning), "Mod details identify the actual untested patch");
            mods.SelectedIndex = -1; Pump();
            Capture((FrameworkElement)window.Content, Path.Combine(output, "designer-10.5.1.png"));
            foreach (bool preview in new[] { true, false })
            {
                window.SaveSettings(window.Settings with { PreviewChanges = preview });
                var plan = new Manager(target).PrepareProfile(ModProfile.Create("Version prompt fixture"), []);
                Exception? failure = null;
                window.Dispatcher.BeginInvoke(() =>
                {
                    var dialog = window.OwnedWindows.Cast<Window>().Single();
                    try
                    {
                        Check(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "Q-SYS Designer 10.5.1\n" + DesignerBuild.UntestedWarning), "Change prompt names 10.5.1 with previews " + preview);
                        Check(dialog.Title == (preview ? "Preview change" : "Untested Designer version"), "Mandatory opt-in remains with previews " + preview);
                    }
                    catch (Exception error) { failure = error; }
                    finally { dialog.DialogResult = false; }
                }, DispatcherPriority.ApplicationIdle);
                ((Task)typeof(MainWindow).GetMethod("ExecutePlan", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [plan, "Apply profile", "Synthetic preview only."])!).GetAwaiter().GetResult();
                if (failure is not null) throw failure;
                Check(!Directory.Exists(Path.Combine(target, ".qds-mod-manager")), "Cancel preserves installation with previews " + preview);
            }
            File.Delete(host);
            Check(DesignerInstallation.Read(target).Label == "Q-SYS Designer 10.5.0", "Legacy installation can read executable version when no host DLL exists");
            File.WriteAllBytes(host, [1, 2, 3]);
            Check(DesignerInstallation.Read(target).Label == "Designer version unavailable", "Unreadable host metadata never borrows an unrelated executable version");
            File.Delete(host); File.Delete(executable);
            Check(DesignerInstallation.Read(target).Label == "Designer version unavailable", "Missing metadata does not infer version from folder name");
            File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Passed {checks.Count} Designer version and warning checks."); return 0;
            T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); Console.Error.WriteLine(error); return 1; }
        finally { window?.Close(); app.Shutdown(); }
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Capture(FrameworkElement content, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
