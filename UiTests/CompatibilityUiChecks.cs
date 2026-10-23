using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using QdsModManager;
using QdsModManager.App;

internal static class CompatibilityUiChecks
{
    internal static int Run(string directory)
    {
        string output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow? window = null;
        try
        {
            string target = Path.Combine(output, "untested-designer"); Directory.CreateDirectory(target);
            File.WriteAllBytes(Path.Combine(target, "Q-Sys Designer.dll"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(target, "ModuleCommon.dll"), [4, 5, 6]);
            window = new MainWindow(Path.Combine(output, "user-data")) { Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            window.SmokeSetup("Light", Catalog.Current); window.Show(); window.SelectTarget(target); Pump();
            T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var mods = Field<DataGrid>("mods"); var scope = Field<ComboBox>("scope"); var versions = Field<ComboBox>("versions");
            var compatibility = Field<TextBlock>("compatibility");
            Check(mods.Items.Count > 0, "Untested installation loads the mod list");
            Check(compatibility.Visibility == Visibility.Visible && compatibility.Text == DesignerBuild.UntestedWarning, "Exact untested warning remains visible in the installation panel");
            mods.SelectedIndex = 0; Pump();
            Check(Field<Button>("install").IsEnabled, "Untested catalog package can be installed");
            Check(versions.Items.Cast<VersionChoice>().All(c => c.Label.Contains("untested")), "Untested versions are labeled without claiming compatibility");
            Check(Field<TextBlock>("availability").Text.Contains(DesignerBuild.UntestedWarning), "Mod details show the exact warning");
            scope.SelectedIndex = 1; Pump(); Check(mods.Items.Count == 0, "Compatible filter does not certify untested packages");
            scope.SelectedIndex = 0; Pump(); Check(mods.Items.Count > 0, "All filter restores untested packages");

            var plan = new Manager(target).PrepareProfile(ModProfile.Create("Preview test"), []);
            foreach (bool accept in new[] { false, true })
            {
                Exception? callbackError = null;
                window.Dispatcher.BeginInvoke(() =>
                {
                    var dialog = window.OwnedWindows.Cast<Window>().Single();
                    try
                    {
                        var labels = Descendants(dialog).OfType<TextBlock>().Select(t => t.Text).ToArray();
                        Check(labels.Contains(DesignerBuild.UntestedWarning), "Change preview shows the exact warning before " + (accept ? "confirmation" : "cancellation"));
                        Check(labels.Contains(target), "Warning preview identifies the selected installation");
                    }
                    catch (Exception error) { callbackError = error; }
                    finally { dialog.DialogResult = accept; }
                }, DispatcherPriority.ApplicationIdle);
                var task = (Task)typeof(MainWindow).GetMethod("ExecutePlan", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [plan, "Apply profile", "No mod files change."])!;
                task.GetAwaiter().GetResult(); if (callbackError is not null) throw callbackError;
                Check(!Directory.Exists(Path.Combine(target, ".qds-mod-manager")), "Preview test preserves the installation after " + (accept ? "smoke confirmation" : "cancellation"));
            }
            Check(Field<TextBlock>("message").Text.StartsWith("Preview mode:"), "Confirmed warning continues to execution in smoke mode");
            window.SmokeSetup("Dark", Catalog.Current); window.SelectTarget(target); Pump();
            Check(compatibility.Visibility == Visibility.Visible && compatibility.Text == DesignerBuild.UntestedWarning, "Warning survives dark theme and refresh");
            File.Delete(Path.Combine(target, "ModuleCommon.dll")); window.SelectTarget(target); Pump();
            Check(!Field<Button>("install").IsEnabled && compatibility.Text.Contains("incomplete"), "Missing Designer file still blocks UI installation");
            File.WriteAllBytes(Path.Combine(output, "checks.json"), Data.Bytes(checks));
            Console.WriteLine($"Passed {checks.Count} native WPF compatibility checks."); return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); Console.Error.WriteLine(error); return 1; }
        finally { window?.Close(); app.Shutdown(); }
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
