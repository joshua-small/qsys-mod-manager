using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using QdsModManager;

if (args.Length == 4 && args[0] == "--self-update-tests") { await SelfUpdateTests.Run(args[1], args[2], args[3]); return; }
if (args.Length == 6 && args[0] == "--legacy-migration") { SelfUpdateTests.Legacy(args[1], args[2], args[3], args[4], args[5]); return; }
if (args.Length == 3 && args[0] == "--self-update-crash")
{
    var updater = new SelfUpdater(_ => false) { AfterWrite = _ => Environment.Exit(73) };
    updater.Apply(Data.Read<SelfUpdatePlan>(File.ReadAllBytes(args[1])), File.ReadAllBytes(args[2])); return;
}
if (args.Length == 1 && args[0] == "--manager-online")
{
    using var client = new ManagerReleaseClient(); var release = await client.Latest(default);
    await client.VerifyPublished(release, default); var archive = await client.Download(release, default);
    Console.WriteLine($"Verified published manager {release.Version}: {archive.Length} bytes, {release.Sha256}"); return;
}
byte[] host = Encoding.UTF8.GetBytes("test host identity"), common = Encoding.UTF8.GetBytes("test common identity");
if (args.Length == 3 && args[0] == "--approval-tests")
{
    if (Directory.Exists(args[2])) throw new IOException("Use a fresh output directory.");
    Directory.CreateDirectory(args[2]);
    var source = Directory.GetFiles(args[1], "*.qdsmod").Select(ModPackage.Open).ToArray();
    var results = new List<string>();
    ApprovalChecks.Run(args[2], source.Where(p => p.Manifest.Id == "schematic-tools" && p.Manifest.Schema == 1).MaxBy(p => Version.Parse(p.Manifest.Version))!,
        source.Where(p => p.Manifest.Id == "automatic-geometry" && p.Manifest.Schema == 1).MaxBy(p => Version.Parse(p.Manifest.Version))!, host, common, results);
    File.WriteAllBytes(Path.Combine(args[2], "results.json"), Data.Bytes(results));
    Console.WriteLine(results.Count + " approval and markerless migration checks passed."); return;
}
if (args.Length == 3 && args[0] == "--seed-preview")
{
    string fixture = Path.GetFullPath(args[1]);
    if (Directory.Exists(Path.Combine(fixture, ".qds-mod-manager"))) throw new IOException("Preview seeding requires a fresh, copied Designer fixture.");
    var source = Directory.GetFiles(args[2], "*.qdsmod").Select(ModPackage.Open).ToArray();
    var pins = source.GroupBy(p => p.Manifest.Id).Select(g => g.OrderBy(p => Version.Parse(p.Manifest.Version)).First()).Select(p => new InstalledMod(p.Manifest.Id, p.Manifest.Version, p.ArchiveHash, p.Manifest.Id == "schematic-tools"));
    var engine = new Manager(fixture); engine.Apply(engine.PrepareProfile(ModProfile.Create("Native preview · pinned set", pins), source)); Console.WriteLine("Isolated native profile fixture prepared."); return;
}
if (args.Length == 2 && args[0] == "--online")
{
    string directory = Path.GetFullPath(args[1]); if (Directory.Exists(directory)) throw new IOException("Use a new download test directory.");
    using var catalog = new OnlineCatalog(directory); var snapshot = await catalog.Fetch(default);
    if (snapshot.Offline) throw new Exception("Live catalog required.");
    var verified = new List<object>();
    foreach (var item in snapshot.Document.Mods) foreach (var release in item.Releases)
    {
        var package = await catalog.Download(item, release, Path.Combine(directory, "packages"), default);
        verified.Add(new { id = item.Id, version = package.Manifest.Version, sha256 = package.ArchiveHash });
    }
    File.WriteAllBytes(Path.Combine(directory, "result.json"), Data.Bytes(new { status = "live downloads verified", verified }));
    Console.WriteLine($"{verified.Count} live catalog packages downloaded and verified."); return;
}
Manager Engine(string root, bool running = false) => new(root, Data.Hash(host), Data.Hash(common), _ => running);
// Native-only helper for testing the app's elevated worker with an actual
// production-host preview. Preparing a plan never changes the installation.
if (args.Length == 6 && args[0] == "--prepare")
{
    var plan = new Manager(args[2]).Prepare(args[1], args[3], args[4] == "-" ? null : args[4]);
    using var request = new FileStream(args[5], FileMode.CreateNew); request.Write(Data.Bytes(plan));
    Console.WriteLine($"Prepared {plan.Action}: {plan.ChangedFiles.Length} mod files change."); return;
}
if (args.Length == 3 && args[0] == "--crash")
{
    var engine = Engine(args[1]); engine.AfterWrite = _ => Environment.Exit(71);
    engine.Apply(engine.Prepare("Install", "schematic-tools", args[2])); return;
}
if (args.Length == 4 && args[0] == "--group-crash")
{
    var engine = Engine(args[1]); var plan = Data.Read<OperationPlan>(File.ReadAllBytes(args[2]));
    engine.AfterWrite = n => { if (n == int.Parse(args[3])) Environment.Exit(72); }; engine.Apply(plan); return;
}
if (args.Length != 3) throw new ArgumentException("Usage: tests <package-directory> <new-output-directory> <unit|lab-installation-path>");
string output = Path.GetFullPath(args[1]); if (Directory.Exists(output)) throw new IOException("Use a fresh output directory."); Directory.CreateDirectory(output);
var packages = Directory.GetFiles(args[0], "*.qdsmod").Select(ModPackage.Open).ToArray();
var schematic = packages.Where(p => p.Manifest.Id == "schematic-tools").OrderBy(p => Version.Parse(p.Manifest.Version)).ToArray();
if (schematic.Length < 2) throw new Exception("Tests require two distinct valid Schematic Tools package versions (the CI older version is a synthetic fixture).");
var old = schematic.First(); var latest = schematic.Last(); var geometry = packages.Where(p => p.Manifest.Id == "automatic-geometry").OrderBy(p => Version.Parse(p.Manifest.Version)).Last();
var checks = new List<string>();
void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
void Reject(Action action, string label) { try { action(); } catch (Exception) { checks.Add(label); return; } throw new Exception("Accepted: " + label); }
string Fresh(string name)
{
    string root = Path.Combine(output, name); Directory.CreateDirectory(root); File.WriteAllBytes(Path.Combine(root, "Q-Sys Designer.dll"), host); File.WriteAllBytes(Path.Combine(root, "ModuleCommon.dll"), common);
    File.WriteAllText(Path.Combine(root, "Designer.Modules.Unrelated.dll"), "untouched unrelated fixture"); return root;
}
Dictionary<string, string?> Files(string root) => Catalog.FileNames.ToDictionary(n => n, n => File.Exists(Path.Combine(root, n)) ? Data.HashFile(Path.Combine(root, n)) : null);
void Same(Dictionary<string, string?> before, string root, string label) => Check(before.OrderBy(p => p.Key).SequenceEqual(Files(root).OrderBy(p => p.Key)), label);
void Do(Manager engine, string action, string id = "schematic-tools", ModPackage? package = null) => Check(engine.Apply(engine.Prepare(action, id, package?.Path)).Success, action + " " + id);

if (args[2] != "unit")
{
    string root = Path.GetFullPath(args[2]); var engine = new Manager(root); var before = Files(root);
    Check(!engine.Inspect(packages).Running, "Lab Designer must be closed");
    Do(engine, "Adopt", package: latest); Do(engine, "Adopt", "automatic-geometry", geometry);
    Same(before, root, "Adoption preserves all native lab mod/dependency bytes");
    Do(engine, "Disable", "automatic-geometry"); Do(engine, "Enable", "automatic-geometry");
    Do(engine, "Install", package: old); Do(engine, "Install", package: latest); Do(engine, "Undo"); Do(engine, "Undo");
    Do(engine, "Remove", "automatic-geometry"); Do(engine, "Undo");
    Same(before, root, "Native lab lifecycle restores the exact original mod files");
    File.WriteAllBytes(Path.Combine(output, "result.json"), Data.Bytes(new { status = "complete", scope = "closed native lab installation; no Designer launch or design changes", checks }));
    Console.WriteLine($"{checks.Count} native lab filesystem checks passed."); return;
}

var root1 = Fresh("lifecycle"); var manager = Engine(root1);
Do(manager, "Install", package: old); Check(manager.Inspect(packages).Mods.Single(m => m.Id == "schematic-tools").Version == old.Manifest.Version, "Old version identified");
Do(manager, "Install", "automatic-geometry", geometry); Do(manager, "Disable");
Check(!File.Exists(Path.Combine(root1, Catalog.Mod("schematic-tools").Module)) && File.Exists(Path.Combine(root1, Catalog.Mod("automatic-geometry").Module)), "Disable isolates the selected bundle");
Check(Data.HashFile(Path.Combine(root1, Catalog.Harmony)) == Catalog.HarmonyHash, "Shared Harmony preserved on disable");
Do(manager, "Enable"); Do(manager, "Install", package: latest);
Check(manager.Inspect(packages).Mods.Single(m => m.Id == "schematic-tools").Version == latest.Manifest.Version, "Update recorded new version");
Do(manager, "Undo"); Check(manager.Inspect(packages).Mods.Single(m => m.Id == "schematic-tools").Version == old.Manifest.Version, "Undo restores prior version");
Do(manager, "Remove"); Do(manager, "Remove", "automatic-geometry");
Check(File.Exists(Path.Combine(root1, Catalog.Harmony)) && File.Exists(Path.Combine(root1, Catalog.License)), "Shared files retained after last mod removal");
Check(File.ReadAllText(Path.Combine(root1, "Designer.Modules.Unrelated.dll")) == "untouched unrelated fixture", "Unknown module preserved");
Check(File.ReadAllBytes(Path.Combine(root1, "Q-Sys Designer.dll")).SequenceEqual(host), "Vendor host preserved");

string adoption = Fresh("adoption"); foreach (var (name, bytes) in latest.Files) File.WriteAllBytes(Path.Combine(adoption, name), bytes);
var baseline = Files(adoption); var adoptEngine = Engine(adoption, true); var adoptPlan = adoptEngine.Prepare("Adopt", "schematic-tools", latest.Path);
Check(adoptPlan.ChangedFiles.Length == 0, "Adoption previews zero file changes"); adoptEngine.Apply(adoptPlan); Same(baseline, adoption, "Adoption allowed while running and leaves mod bytes untouched");
Reject(() => adoptEngine.Apply(adoptEngine.Prepare("Disable", "schematic-tools")), "File changes blocked while Designer is running"); Same(baseline, adoption, "Running-process rejection preserves files");

string stale = Fresh("stale"); var staleEngine = Engine(stale); var stalePlan = staleEngine.Prepare("Install", "schematic-tools", latest.Path);
File.WriteAllText(Path.Combine(stale, Catalog.Mod("schematic-tools").Marker), "external change");
Reject(() => staleEngine.Apply(stalePlan), "Stale preview rejected");
Check(File.ReadAllText(Path.Combine(stale, Catalog.Mod("schematic-tools").Marker)) == "external change", "External file preserved");
Reject(() => staleEngine.Prepare("Install", "schematic-tools", latest.Path), "Unknown existing marker not overwritten");
string badHost = Fresh("wrong-host"); File.WriteAllText(Path.Combine(badHost, "ModuleCommon.dll"), "changed"); Reject(() => Engine(badHost).Prepare("Install", "schematic-tools", latest.Path), "Wrong host rejected");
string badDependency = Fresh("wrong-dependency"); File.WriteAllText(Path.Combine(badDependency, Catalog.Harmony), "other version"); Reject(() => Engine(badDependency).Prepare("Install", "schematic-tools", latest.Path), "Different shared dependency not overwritten");

string failing = Fresh("rollback"); var failingEngine = Engine(failing); var failureBefore = Files(failing);
failingEngine.AfterWrite = _ => throw new IOException("Injected failure after write");
Reject(() => failingEngine.Apply(failingEngine.Prepare("Install", "schematic-tools", latest.Path)), "Injected failure surfaces"); Same(failureBefore, failing, "Failed transaction restores every original file");
Check(failingEngine.Inspect(packages).Pending is null, "Successful rollback does not leave pending transaction");

string crash = Fresh("interrupted"); var crashBefore = Files(crash);
var processInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") processInfo.ArgumentList.Add(typeof(Manager).Assembly.Location.Replace("QdsModManager.Core.dll", "QdsModManager.Tests.dll"));
foreach (var arg in new[] { "--crash", crash, latest.Path }) processInfo.ArgumentList.Add(arg);
using (var child = Process.Start(processInfo)!) { child.WaitForExit(); Check(child.ExitCode == 71, "Crash injected after first native file write"); }
var recovery = Engine(crash); Check(recovery.Inspect(packages).Pending is not null, "Interrupted journal detected");
Reject(() => recovery.Prepare("Install", "schematic-tools", latest.Path), "New changes blocked until recovery");
Do(recovery, "Recover"); Same(crashBefore, crash, "Interrupted transaction recovered from verified backups");

string backupTest = Fresh("damaged-backup"); var backupEngine = Engine(backupTest); Do(backupEngine, "Install", package: old);
var crashInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") crashInfo.ArgumentList.Add(typeof(Manager).Assembly.Location.Replace("QdsModManager.Core.dll", "QdsModManager.Tests.dll"));
foreach (var arg in new[] { "--crash", backupTest, latest.Path }) crashInfo.ArgumentList.Add(arg);
using (var child = Process.Start(crashInfo)!) { child.WaitForExit(); Check(child.ExitCode == 71, "Update interruption injected"); }
string oldModule = Catalog.Mod("schematic-tools").Module;
string backupObject = Path.Combine(backupTest, ".qds-mod-manager", "objects", Data.Hash(old.Files[oldModule]));
File.WriteAllText(backupObject, "damaged backup"); var beforeRecovery = Files(backupTest);
Reject(() => backupEngine.Apply(backupEngine.Prepare("Recover")), "Corrupt backup rejected before restoring any file");
Same(beforeRecovery, backupTest, "Corrupt-backup recovery leaves destination files untouched");
File.WriteAllBytes(backupObject, old.Files[oldModule]); Do(backupEngine, "Recover");
Check(Data.HashFile(Path.Combine(backupTest, oldModule)) == Data.Hash(old.Files[oldModule]), "Repaired backup restores previous version");

string changed = Fresh("changed-managed"); var changedEngine = Engine(changed); Do(changedEngine, "Install", package: latest);
File.WriteAllText(Path.Combine(changed, Catalog.Mod("schematic-tools").Module), "external replacement"); Reject(() => changedEngine.Prepare("Remove", "schematic-tools"), "Modified managed file not deleted");
Reject(() => changedEngine.Prepare("Undo"), "Undo refuses external changes");
string linkRoot = Fresh("linked-target"); string external = Path.Combine(output, "outside-marker"); File.WriteAllText(external, "preserve");
try { File.CreateSymbolicLink(Path.Combine(linkRoot, Catalog.Mod("schematic-tools").Marker), external); Reject(() => Engine(linkRoot).Prepare("Install", "schematic-tools", latest.Path), "Symlink destination rejected"); }
catch (UnauthorizedAccessException) { checks.Add("Symlink creation unavailable on this host; link guard not native-tested here"); }

void BadPackage(string name, Action<ZipArchive> mutation)
{
    string path = Path.Combine(output, name + ".qdsmod"); File.Copy(latest.Path, path);
    using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) mutation(zip);
    Reject(() => ModPackage.Open(path), name);
}
BadPackage("traversal-entry-rejected", zip => { using var s = zip.CreateEntry("../escape.dll").Open(); s.WriteByte(1); });
BadPackage("duplicate-entry-rejected", zip => { using var s = zip.CreateEntry("manifest.json").Open(); s.WriteByte(1); });
BadPackage("tampered-payload-rejected", zip => { var entry = zip.GetEntry("payload/" + Catalog.Harmony)!; entry.Delete(); using var s = zip.CreateEntry("payload/" + Catalog.Harmony).Open(); s.WriteByte(1); });
BadPackage("unsupported-file-rejected", zip =>
{
    var entry = zip.GetEntry("manifest.json")!; PackageManifest manifest;
    using (var s = entry.Open()) using (var memory = new MemoryStream()) { s.CopyTo(memory); manifest = Data.Read<PackageManifest>(memory.ToArray()); }
    entry.Delete(); manifest.Files[0] = manifest.Files[0] with { Name = "Q-Sys Designer.dll" };
    using var target = zip.CreateEntry("manifest.json").Open(); target.Write(Data.Bytes(manifest));
});
await CatalogChecks.Run(latest, output, checks);
ProfileChecks.Run(output, old, latest, geometry, host, common, checks);
ApprovalChecks.Run(output, latest, geometry, host, common, checks);
File.WriteAllBytes(Path.Combine(output, "result.json"), Data.Bytes(new { status = "complete", checks }));
Console.WriteLine($"{checks.Count} package, transaction and recovery checks passed.");
