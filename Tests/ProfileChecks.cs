using QdsModManager;
using System.Diagnostics;

internal static class ProfileChecks
{
    public static void Run(string output, ModPackage old, ModPackage latest, ModPackage geometry, byte[] host, byte[] common, List<string> checks)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        void Reject(Action action, string label) { try { action(); } catch { checks.Add(label); return; } throw new Exception(label); }
        Manager Fresh(string name, bool running = false)
        {
            string root = Path.Combine(output, "profiles", name); Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "Q-Sys Designer.dll"), host); File.WriteAllBytes(Path.Combine(root, "ModuleCommon.dll"), common);
            File.WriteAllText(Path.Combine(root, "Designer.Modules.Unrelated.dll"), "preserve");
            return new(root, Data.Hash(host), Data.Hash(common), _ => running);
        }
        var all = new[] { old, latest, geometry };
        ModProfile Profile(string name, ModPackage p, bool enabled = true) => ModProfile.Create(name, [new(p.Manifest.Id, p.Manifest.Version, p.ArchiveHash, enabled), new(geometry.Manifest.Id, geometry.Manifest.Version, geometry.ArchiveHash, true)]);
        var initial = Profile("Initial", old); var next = Profile("Updated", latest, false); var manager = Fresh("first"); var other = Fresh("second");
        var plan = manager.PrepareProfile(initial, all); Check(!manager.Inspect(all).Mods.Any(m => m.Managed), "Preparing and canceling a profile changes nothing");
        manager.Apply(plan); other.Apply(other.PrepareProfile(initial, all));
        var baseline = manager.Inspect(all); manager.Apply(manager.PrepareProfile(next, all));
        Check(manager.Inspect(all).Mods.Single(m => m.Id == old.Manifest.Id).Version == latest.Manifest.Version && !manager.Inspect(all).Mods.Single(m => m.Id == old.Manifest.Id).Enabled, "Group restores exact pins and disabled state");
        Check(other.Inspect(all).Profile!.Mods.Single(m => m.Id == old.Manifest.Id).Version == old.Manifest.Version, "Another installation retains its applied snapshot");
        manager.Apply(manager.Prepare("Undo")); Check(manager.Inspect(all).Stamp == baseline.Stamp, "Whole-group undo restores files and profile metadata");
        manager.Apply(manager.PrepareProfile(initial, all, true, "Suspend"));
        Check(manager.Inspect(all).Suspended && manager.Inspect(all).Profile!.Mods.All(m => m.Enabled) && manager.Inspect(all).Mods.All(m => !m.Enabled), "Suspension preserves profile enabled intent");
        Check(manager.Inspect(all).Unmanaged!.Contains("Designer.Modules.Unrelated.dll"), "Unmanaged files are reported during suspension");
        manager.Apply(manager.PrepareProfile(initial, all, false, "Resume"));
        Check(manager.Inspect(all).Stamp == baseline.Stamp, "Resume restores the enabled set exactly");
        Reject(() => manager.PrepareProfile(next, [old, geometry]), "Missing exact package blocks preparation");
        Reject(() => manager.PrepareProfile(next with { HostSha256 = new string('0', 64) }, all), "Incompatible profile blocks before writes");
        var library = new ProfileLibrary(Path.Combine(output, "profile-library")); library.Reconcile(manager.Inspect(all)); library.Reconcile(other.Inspect(all));
        Reject(() => library.Delete(initial.Id), "Assigned profile cannot be deleted");
        var revised = initial with { Mods = initial.Mods.Select(p => p.Id == latest.Manifest.Id ? p with { Version = latest.Manifest.Version, Sha256 = latest.ArchiveHash } : p).ToArray() };
        manager.Apply(manager.PrepareProfile(revised, all)); library.Reconcile(manager.Inspect(all)); library.Reconcile(other.Inspect(all));
        Check(library.Store.Profiles.Single(p => p.Id == initial.Id).Mods.Single(p => p.Id == latest.Manifest.Id).Version == latest.Manifest.Version, "Unchanged second installation does not overwrite newer saved pins");
        manager.Apply(manager.Prepare("Undo")); library.Reconcile(manager.Inspect(all));
        Check(library.Store.Profiles.Single(p => p.Id == initial.Id).Mods.Single(p => p.Id == latest.Manifest.Id).Version == old.Manifest.Version, "Undo reconciles prior profile pins into library");
        Check(manager.CachedPackages().Any(p => p.ArchiveHash == latest.ArchiveHash) && manager.CachedPackages().Any(p => p.ArchiveHash == old.ArchiveHash), "Installation cache retains historical exact pins");
        string exported = Path.Combine(output, "roundtrip.qdsprofile"); File.WriteAllBytes(exported, Data.Bytes(next)); var imported = ModProfile.Import(exported);
        Check(imported.Id != next.Id && imported.Mods.SequenceEqual(next.Mods), "Profile import preserves exact metadata and creates a fresh identity"); library.Save(imported); library.Delete(imported.Id);
        for (int fail = 1; fail <= 5; fail++)
        {
            var engine = Fresh("failure-" + fail); engine.Apply(engine.PrepareProfile(initial, all)); string stamp = engine.Inspect(all).Stamp;
            engine.AfterWrite = n => { if (n == fail) throw new IOException("group failure"); };
            var empty = ModProfile.Create("Empty"); Reject(() => engine.Apply(engine.PrepareProfile(empty, all)), "Group failure injected after file " + fail);
            Check(engine.Inspect(all).Stamp == stamp && engine.Inspect(all).Pending is null, "Complete profile rollback after file " + fail);
        }
        var running = Fresh("running", true); Reject(() => running.Apply(running.PrepareProfile(initial, all)), "Running Designer blocks grouped changes");
        foreach (int stop in new[] { 1, 5 })
        {
            var interrupted = Fresh("crash-" + stop); interrupted.Apply(interrupted.PrepareProfile(initial, all)); var original = interrupted.Inspect(all);
            var crashPlan = interrupted.PrepareProfile(ModProfile.Create("Empty"), all); string request = Path.Combine(output, "group-crash-" + stop + ".json"); File.WriteAllBytes(request, Data.Bytes(crashPlan));
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(typeof(ProfileChecks).Assembly.Location);
            foreach (string arg in new[] { "--group-crash", original.Target, request, stop.ToString() }) start.ArgumentList.Add(arg);
            using (var child = Process.Start(start)!) { child.WaitForExit(); Check(child.ExitCode == 72, "Grouped crash injected at write " + stop); }
            Check(interrupted.Inspect(all).Pending is not null, "Grouped interruption blocks new operations at write " + stop);
            interrupted.Apply(interrupted.Prepare("Recover")); Check(interrupted.Inspect(all).Stamp == original.Stamp, "Grouped recovery restores profile and files at write " + stop);
        }
        var stale = manager.PrepareProfile(next, all); manager.Apply(manager.Prepare("Disable", old.Manifest.Id)); Reject(() => manager.Apply(stale), "Grouped stale preview rejected");
        Check(!manager.Inspect(all).Profile!.Mods.Single(m => m.Id == old.Manifest.Id).Enabled, "Individual changes revise active profile pins");
        string root1 = manager.Inspect(all).Target, statePath = Path.Combine(root1, ".qds-mod-manager", "state.json");
        var state = Data.Read<RegistryState>(File.ReadAllBytes(statePath)); File.WriteAllBytes(statePath, Data.Bytes(new { schema = 1, mods = state.Mods }));
        var migrated = manager.Inspect(all); var beforeMigration = File.ReadAllBytes(statePath); library.Reconcile(migrated);
        Check(migrated.Profile!.Mods.Length == 2 && beforeMigration.SequenceEqual(File.ReadAllBytes(statePath)), "0.5.1 migration captures a profile without rewriting installation state");
        Check(File.ReadAllText(Path.Combine(root1, "Designer.Modules.Unrelated.dll")) == "preserve" && File.ReadAllBytes(Path.Combine(root1, "Q-Sys Designer.dll")).SequenceEqual(host), "Grouped operations preserve unmanaged and vendor files");
    }
}
