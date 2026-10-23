using System.IO.Compression;
using System.Reflection;
using System.Text;
using QdsModManager;

internal static class InstallFlowChecks
{
    internal static void Run(string modules, string existingPackages, string directory)
    {
        string output = Path.GetFullPath(directory);
        if (Directory.Exists(output)) throw new IOException("Use a fresh output directory.");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        string Reject(Action action, string label)
        {
            try { action(); } catch (Exception error) { checks.Add(label); return error.Message; }
            throw new Exception("Accepted: " + label);
        }
        var originals = Catalog.Approvals;
        var definitions = new[] { ("mods-core", "Q-SYS Mods Core", "ModsCore"), ("compact-connectors", "Compact Connectors", "CompactConnectors"), ("component-auto-width", "Component Auto Width", "ComponentAutoWidth") }
            .Select(m => new ModDefinition(m.Item1, m.Item2, "fixtures/" + m.Item1, "Designer.Modules." + m.Item3 + ".dll", null,
                [new(3, [new("Designer.Modules." + m.Item3 + ".dll", false)], m.Item1 == "mods-core" ? [] : [new("mods-core", "0.1.7", "0.2.0")],
                    m.Item1 == "mods-core" ? [] : [new("automatic-geometry", "0.0.0"), new("schematic-tools", "0.0.0", "0.17.0")], "0.1.0")])).ToArray();
        Catalog.ForTool(originals with { Mods = originals.Mods.Concat(definitions).ToArray() });
        File.WriteAllBytes(Path.Combine(output, "approvals.json"), Data.Bytes(Catalog.Approvals));
        string packageDirectory = Path.Combine(output, "packages"); Directory.CreateDirectory(packageDirectory);
        foreach (var definition in definitions)
        {
            string dll = Path.Combine(modules, definition.Id, definition.Module); byte[] bytes = File.ReadAllBytes(dll);
            var layout = definition.Layouts.Single();
            var manifest = new PackageManifest(3, definition.Id, definition.Name, AssemblyName.GetAssemblyName(dll).Version!.ToString(), "Synthetic installation fixture; never load in Designer.",
                Catalog.HostHash, Catalog.CommonHash, [new(definition.Module, Data.Hash(bytes), bytes.Length, false)], layout.Dependencies, layout.Conflicts);
            using var zip = ZipFile.Open(Path.Combine(packageDirectory, definition.Id + ".qdsmod"), ZipArchiveMode.Create);
            using (var entry = zip.CreateEntry("manifest.json").Open()) entry.Write(Data.Bytes(manifest));
            using (var entry = zip.CreateEntry("payload/" + definition.Module).Open()) entry.Write(bytes);
        }
        var packages = Directory.GetFiles(packageDirectory, "*.qdsmod").Concat(Directory.GetFiles(existingPackages, "*.qdsmod")).Select(ModPackage.Open).ToArray();
        ModPackage Package(string id) => packages.Where(p => p.Manifest.Id == id).MaxBy(p => Version.Parse(p.Manifest.Version))!;
        var core = Package("mods-core"); var connectors = Package("compact-connectors"); var width = Package("component-auto-width"); var geometry = Package("automatic-geometry");
        File.Copy(geometry.Path, Path.Combine(packageDirectory, "automatic-geometry.qdsmod"));
        var choices = packages.Select(DependencyChoice.From).ToArray(); var selected = new[] { connectors, width };
        var resolved = Requirements.ResolveChoices([], selected.Select(DependencyChoice.From), choices);
        Check(resolved.Select(p => p.Id).Order().SequenceEqual(new[] { "compact-connectors", "component-auto-width", "mods-core" }), "Multiple selections share one required Core");
        string missing = Reject(() => Requirements.ResolveChoices([], selected.Select(DependencyChoice.From), choices.Where(p => p.Id != "mods-core")), "Missing Core blocks preparation");
        Check(missing.Contains("Q-SYS Mods Core") && missing.Contains("0.1.7") && !missing.Contains("migration"), "Missing dependency identifies its name and required range");
        string conflict = Reject(() => Requirements.ResolveChoices([DependencyChoice.From(geometry)], selected.Select(DependencyChoice.From), choices), "Automatic Geometry conflict blocks the batch");
        Check(conflict.Contains("Disable Automatic Geometry") && !conflict.Contains("Schematic Tools"), "Conflict identifies Automatic Geometry without irrelevant migration advice");
        Reject(() => Requirements.ResolveChoices([], [DependencyChoice.From(core), DependencyChoice.From(core)], choices), "Duplicate explicit selections rejected");
        Reject(() => Requirements.ResolveChoices([], [DependencyChoice.From(connectors), DependencyChoice.From(core) with { Version = "0.1.0.0" }], choices), "Incompatible explicitly selected Core is never silently replaced");
        var legacy = Package("schematic-tools");
        string legacyMessage = Reject(() => Requirements.ResolveChoices([DependencyChoice.From(legacy) with { Version = "0.16.0" }], [DependencyChoice.From(connectors)], choices), "Legacy bundle conflict blocks installation");
        Check(legacyMessage.Contains("Split Schematic Tools"), "Migration advice is retained for the actual legacy bundle");
        byte[] host = Encoding.UTF8.GetBytes("untested install flow host"), common = Encoding.UTF8.GetBytes("untested install flow common");
        Manager Engine(string name, bool tested = false, bool running = false)
        {
            string root = Path.Combine(output, name); Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "Q-Sys Designer.dll"), host); File.WriteAllBytes(Path.Combine(root, "ModuleCommon.dll"), common);
            return new(root, tested ? Data.Hash(host) : Catalog.HostHash, tested ? Data.Hash(common) : Catalog.CommonHash, _ => running);
        }
        void Apply(Manager manager, OperationPlan plan) => Check(manager.Apply(plan with { UntestedVersionAcknowledged = true }).Success, plan.Action + " succeeds");
        var manager = Engine("opted-in"); var plan = manager.PrepareInstall(selected, packages);
        Check(!Directory.Exists(Path.Combine(plan.Target, ".qds-mod-manager")), "Batch preparation creates no installation state");
        Reject(() => manager.Apply(plan), "Untested batch requires acknowledgement");
        Check(!Directory.Exists(Path.Combine(plan.Target, ".qds-mod-manager")), "Refusing acknowledgement leaves the installation untouched");
        Apply(manager, plan);
        var installed = manager.Inspect(packages);
        Check(installed.Mods.Where(p => p.Managed && p.Enabled).Count() == 3, "Batch installs both features and Core together");
        var consent = installed.UntestedRuntime!;
        Check(consent == new UntestedRuntimeOptIn(1, plan.Target, Data.Hash(host), Data.Hash(common)), "Shared consent binds the actual installation and both host hashes");
        Check(!Encoding.UTF8.GetString(Data.Bytes(installed.Profile!)).Contains("untestedRuntime"), "Portable profiles do not export runtime consent");
        string state = Path.Combine(plan.Target, ".qds-mod-manager", "state.json"); var before = File.ReadAllBytes(state);
        Apply(manager, manager.PrepareProfile(installed.Profile!, packages, true, "Suspend")); Check(manager.Inspect(packages).UntestedRuntime is null, "Suspension clears runtime consent");
        Apply(manager, manager.Prepare("Undo")); Check(File.ReadAllBytes(state).SequenceEqual(before), "Undo restores exact prior consent and profile");
        var disabled = installed.Profile! with { Mods = installed.Profile!.Mods.Select(p => p with { Enabled = false }).ToArray() };
        Apply(manager, manager.PrepareProfile(disabled, packages)); Check(manager.Inspect(packages).UntestedRuntime is null, "Disabling Core clears consent");
        Apply(manager, manager.PrepareInstall(selected, packages)); Check(manager.Inspect(packages).UntestedRuntime == consent, "Batch re-enables disabled required Core and records consent");
        before = File.ReadAllBytes(state);
        manager.AfterWrite = _ => throw new IOException("Injected failure");
        Reject(() => manager.Apply(manager.PrepareProfile(installed.Profile!, packages, true, "Suspend") with { UntestedVersionAcknowledged = true }), "Failed grouped change reports its error"); manager.AfterWrite = null;
        Check(File.ReadAllBytes(state).SequenceEqual(before) && manager.Inspect(packages).Pending is null, "Rollback preserves consent and all prior pins");
        var atomic = Engine("batch-undo"); Apply(atomic, atomic.PrepareInstall(selected, packages)); Apply(atomic, atomic.Prepare("Undo"));
        Check(atomic.Inspect(packages).Profile!.Mods.Length == 0 && atomic.Inspect(packages).UntestedRuntime is null, "One Undo removes the entire new batch and its consent");
        var tested = Engine("tested", true); tested.Apply(tested.PrepareInstall(selected, packages));
        Check(tested.Inspect(packages).UntestedRuntime is null, "Tested build does not need or write consent");
        var running = Engine("running", running: true); var whileRunning = running.PrepareInstall(selected, packages);
        Reject(() => running.Apply(whileRunning with { UntestedVersionAcknowledged = true }), "Running Designer blocks all batch writes");
        Check(!File.Exists(Path.Combine(whileRunning.Target, core.Manifest.Files[0].Name)), "Running rejection writes no mod files");
        File.WriteAllBytes(Path.Combine(output, "checks.json"), Data.Bytes(checks));
        Console.WriteLine($"Passed {checks.Count} installation flow and shared consent checks.");
    }
}
