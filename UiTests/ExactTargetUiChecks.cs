using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using QdsModManager;
using QdsModManager.App;

internal static class ExactTargetUiChecks
{
    internal static int Run(string packagePath, string directory)
    {
        string output = Path.GetFullPath(directory); if (Directory.Exists(output)) throw new IOException("Use a fresh output directory."); Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; MainWindow? window = null;
        try
        {
            var package = ModPackage.Open(packagePath); string data = Path.Combine(output, "user-data"), cache = Path.Combine(data, "Packages"); Directory.CreateDirectory(cache);
            File.Copy(package.Path, Path.Combine(cache, package.ArchiveHash + ".qdsmod"));
            var item = Catalog.Current.Mods.Single(m => m.Id == package.Manifest.Id);
            var release = item.Releases[0] with { Version = package.Manifest.Version, DesignerVersion = "10.5.1", HostSha256 = PackageCompatibility.Host1051, CommonSha256 = PackageCompatibility.Common1051, Sha256 = package.ArchiveHash, Size = new FileInfo(package.Path).Length };
            var document = Catalog.Current with { Mods = Catalog.Current.Mods.Select(m => m.Id == item.Id ? m with { Releases = [release] } : m).ToArray() };
            window = new MainWindow(data) { Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            window.SmokeSetup("Light", document); window.Show();
            string target = Path.Combine(output, "synthetic-designer"); Directory.CreateDirectory(target);
            File.WriteAllBytes(Path.Combine(target, "Q-Sys Designer.dll"), [1,2,3]); File.WriteAllBytes(Path.Combine(target, "ModuleCommon.dll"), [4,5,6]); window.SelectTarget(target);
            FieldInfo Field(string name) => typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
            T Get<T>(string name) => (T)Field(name).GetValue(window)!;
            object? Invoke(string name) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
            var mods = Get<DataGrid>("mods"); var scope = Get<ComboBox>("scope");
            void Inspect(string host, string common)
            {
                // UI-only fixture. The actual transaction engine is tested separately.
                Field("hostHash").SetValue(window, host); Field("commonHash").SetValue(window, common);
                var before = Get<Inspection>("inspection");
                Field("inspection").SetValue(window, before with { Build = new(host, common, host == Catalog.HostHash && common == Catalog.CommonHash) });
                scope.SelectedIndex = 0; Invoke("Filter"); mods.SelectedItem = mods.Items.Cast<ModRow>().Single(r => r.Id == item.Id);
            }
            foreach (var (host, common, name) in new[] { (Catalog.HostHash, Catalog.CommonHash, "10.5.0"), (Catalog.HostHash, PackageCompatibility.Common1051, "mixed"), (new string('f',64), new string('e',64), "unknown") })
            {
                Inspect(host, common);
                Check(!Get<Button>("install").IsEnabled && !((ModRow)mods.SelectedItem).PrimaryEnabled, name + ": catalog and row actions reject .1-only package");
                Check(Get<ComboBox>("versions").Items.Cast<VersionChoice>().All(c => c.Label.Contains("incompatible")), name + ": cached package is labeled incompatible");
                Check(!((VersionChoice[])Invoke("InstallOptions")!).Any(c => c.Package?.ArchiveHash == package.ArchiveHash || c.Release?.Sha256 == package.ArchiveHash), name + ": dependency and batch choices exclude incompatible cached archive");
                Check(Get<TextBlock>("availability").Text.Contains("Package target: Designer 10.5.1") && Get<TextBlock>("availability").Text.Contains("unavailable"), name + ": details identify exact package target and reject install");
                scope.SelectedIndex = 1; Check(!mods.Items.Cast<ModRow>().Any(r => r.Id == item.Id), name + ": compatible filter excludes .1-only package");
            }
            Inspect(PackageCompatibility.Host1051, PackageCompatibility.Common1051);
            Check(Get<Button>("install").IsEnabled && ((ModRow)mods.SelectedItem).PrimaryEnabled, "Exact .1 pair enables verified catalog/local install choice");
            Check(Get<TextBlock>("availability").Text.Contains(DesignerBuild.UntestedWarning), "Exact .1 package does not remove host consent warning");
            scope.SelectedIndex = 1;
            Check(mods.Items.Cast<ModRow>().Select(r => r.Id).SequenceEqual([item.Id]), "Compatible filter includes exact .1 package without qualifying legacy .0 packages");
            scope.SelectedIndex = 0;
            var current = Get<Inspection>("inspection");
            var old = new InstalledMod(item.Id, "1.0.0.0", new string('a', 64), true);
            Field("inspection").SetValue(window, current with { Profile = ModProfile.Create("Update fixture", [old]),
                Mods = current.Mods.Select(m => m.Id == item.Id ? m with { Managed = true, Enabled = true, Version = old.Version, Status = "Enabled" } : m).ToArray() });
            Inspect(Catalog.HostHash, Catalog.CommonHash);
            Check(((Array)Invoke("AvailableUpdates")!).Length == 0 && !Get<Button>("updates").IsEnabled, "10.5.0 sidebar and review list exclude .1-only update");
            Inspect(PackageCompatibility.Host1051, PackageCompatibility.Common1051);
            Check(((Array)Invoke("AvailableUpdates")!).Length == 1 && Get<Button>("updates").IsEnabled, "Exact .1 sidebar and review list include .1 package update");
            var disabled = new ModRow(item.Id, item.Name, package.Manifest.Version, "99.0.0", "Disabled", new(item.Id, item.Name, package.Manifest.Version, "Disabled", true, false, ""), item) { CanManage = true, CanEnable = false };
            Check(!disabled.PrimaryEnabled && disabled.RemoveEnabled, "Incompatible pinned version cannot be enabled even when another version is available");
            Check((disabled with { Local = disabled.Local! with { Enabled = true } }).PrimaryEnabled, "Already enabled incompatible package can still be disabled");
            File.WriteAllBytes(Path.Combine(output, "checks.json"), Data.Bytes(checks)); Console.WriteLine(checks.Count + " native WPF exact-package checks passed."); return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); Console.Error.WriteLine(error); return 1; }
        finally { window?.Close(); app.Shutdown(); }
    }
}
