using System.IO.Compression;
using System.Text;
using QdsModManager;

internal static class DependencyChecks
{
    internal static void Run(string approvals, string packagesDirectory, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Use a fresh results directory.");
        Directory.CreateDirectory(output); var checks = new List<string>();
        void Check(bool result, string label) { if (!result) throw new Exception(label); checks.Add(label); }
        void Reject(Action action, string label) { try { action(); } catch (Exception) { checks.Add(label); return; } throw new Exception("Accepted: " + label); }
        Catalog.ForTool(Data.Read<ApprovalDocument>(File.ReadAllBytes(approvals)));
        var definition = Catalog.Mod("better-packing"); var layout = definition.Layouts.Single();
        ApprovalDocument With(PackageLayout next) => Catalog.Approvals with { Mods = Catalog.Mods.Select(m => m.Id == definition.Id ? m with { Layouts=m.Layouts.Append(next).ToArray() } : m).ToArray() };
        Reject(() => ApprovalDocument.Validate(With(layout)), "Overlapping versioned layouts rejected");
        ApprovalDocument.Preserve(Catalog.Approvals, With(layout with { MinimumVersion="0.2.0",BeforeVersion="0.3.0" }));
        checks.Add("Future disjoint dependency policies preserve historical layouts");
        var packages = Directory.GetFiles(packagesDirectory,"*.qdsmod").Select(ModPackage.Open).ToArray();
        ModPackage Package(string id) => packages.Where(p => p.Manifest.Id == id).MaxBy(p => Version.Parse(p.Manifest.Version))!;
        var core = Package("mods-core"); var packing = Package("better-packing"); var legacy = packages.Single(p => p.Manifest.Id == "schematic-tools" && Version.Parse(p.Manifest.Version) < new Version(0,17,0)); var geometry = Package("automatic-geometry");
        byte[] host = Encoding.UTF8.GetBytes("dependency test host"), common = Encoding.UTF8.GetBytes("dependency test common");
        Manager Engine(string name)
        {
            var path = Path.Combine(output,name); Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path,"Q-Sys Designer.dll"),host); File.WriteAllBytes(Path.Combine(path,"ModuleCommon.dll"),common);
            return new(path,Data.Hash(host),Data.Hash(common),_ => false);
        }
        var single = Engine("dependencies");
        Reject(() => single.Prepare("Install","better-packing",packing.Path), "Missing enabled dependency blocks direct installation");
        var preview = single.PrepareInstall(packing, packages);
        Check(preview.Group!.Profile.Mods.Select(m => m.Id).Order().SequenceEqual(new[] { "better-packing","mods-core" }), "Dependency install resolves only required packages");
        Check(single.Inspect(packages).Profile!.Mods.Length == 0, "Dependency preview makes no installation changes");
        single.Apply(preview);
        Reject(() => single.Prepare("Disable","mods-core"), "Disabling an in-use Core is blocked");
        Reject(() => single.Prepare("Remove","mods-core"), "Removing an in-use Core is blocked");
        single.Apply(single.Prepare("Disable","better-packing")); single.Apply(single.Prepare("Disable","mods-core"));
        Reject(() => single.Prepare("Enable","better-packing"), "Enabling a consumer without Core is blocked");
        single.Apply(single.Prepare("Enable","mods-core")); single.Apply(single.Prepare("Enable","better-packing"));
        Check(single.Inspect(packages).Mods.Where(m => m.Managed).All(m => m.Enabled), "Compatible dependency re-enable works");
        Reject(() => Requirements.Validate(new[] { packing.Manifest, core.Manifest with { Version="0.2.0.0" } }), "Unsupported Core major/minor range is rejected");
        Reject(() => Requirements.Validate(new[] { packing.Manifest, core.Manifest with { Dependencies=[new("better-packing","0.1.0")] } }), "Dependency cycles are rejected");
        var tampered = Path.Combine(output,"tampered.qdsmod");
        using (var zip = ZipFile.Open(tampered,ZipArchiveMode.Create))
        {
            using (var entry = zip.CreateEntry("manifest.json").Open()) entry.Write(Data.Bytes(packing.Manifest with { Dependencies=[] }));
            foreach (var file in packing.Files) using (var entry = zip.CreateEntry("payload/"+file.Key).Open()) entry.Write(file.Value);
        }
        Reject(() => ModPackage.Open(tampered), "Package cannot omit catalog-required dependencies");
        var manager = Engine("migration");
        manager.Apply(manager.Prepare("Install","schematic-tools",legacy.Path)); manager.Apply(manager.Prepare("Install","automatic-geometry",geometry.Path));
        var original = manager.Inspect(packages); var originalPins = Data.Bytes(original.Profile!);
        var debug = Package("schematic-tools");
        Reject(() => manager.Prepare("Install","schematic-tools",debug.Path), "Direct diagnostics upgrade cannot discard bundled features");
        Reject(() => manager.PrepareInstall(debug,packages), "Resolved diagnostics upgrade requires split migration first");
        var unsafeProfile = original.Profile! with { Mods = original.Profile!.Mods.Select(p => p.Id == "schematic-tools" ? new ProfileMod(debug.Manifest.Id,debug.Manifest.Version,debug.ArchiveHash,p.Enabled) : p).Append(new ProfileMod(core.Manifest.Id,core.Manifest.Version,core.ArchiveHash,true)).ToArray() };
        Reject(() => manager.PrepareProfile(unsafeProfile,packages,action:"Updates"), "Grouped diagnostics upgrade cannot bypass split migration");
        Check(Data.Bytes(manager.Inspect(packages).Profile!).SequenceEqual(originalPins), "Rejected diagnostics upgrades preserve the original bundle");
        Reject(() => manager.PrepareInstall(packing,packages), "Legacy bundle cannot coexist with extracted features");
        var migrate = manager.PrepareSchematicToolsMigration(packages);
        Check(migrate.Group!.Profile.Id == original.Profile!.Id && migrate.Group.Profile.Name == original.Profile.Name, "Migration retains profile identity and name");
        Check(migrate.Group.Profile.Mods.Single(m => m.Id=="automatic-geometry") == original.Profile.Mods.Single(m => m.Id=="automatic-geometry"), "Migration preserves unrelated exact package pins");
        Check(migrate.Group.Profile.Mods.Length==9 && !migrate.Group.Profile.Mods.Any(m=>m.Id=="schematic-tools"), "One bundle becomes eight packages plus unrelated geometry");
        manager.AfterWrite = n => { if(n==3) throw new IOException("Injected migration failure"); };
        Reject(() => manager.Apply(migrate), "Interrupted migration reports failure"); manager.AfterWrite=null;
        Check(Data.Bytes(manager.Inspect(packages).Profile!).SequenceEqual(originalPins), "Failure restores exact original profile");
        Check(manager.Inspect(packages).Mods.Single(m=>m.Id=="schematic-tools").Enabled && manager.Inspect(packages).Pending is null, "Failure restores the legacy module without a pending journal");
        manager.Apply(manager.PrepareSchematicToolsMigration(packages));
        var after = manager.Inspect(packages);
        Check(Manager.SchematicToolsReplacementIds.All(id=>after.Mods.Single(m=>m.Id==id).Enabled), "All migrated packages enabled together");
        Check(!File.Exists(Path.Combine(after.Target,Catalog.Mod("schematic-tools").Module)), "Legacy loader removed before the next launch");
        Check(manager.CachedPackages().Any(p=>p.ArchiveHash==legacy.ArchiveHash), "Historical bundle archive retained for rollback");
        manager.Apply(manager.Prepare("Undo"));
        Check(Data.Bytes(manager.Inspect(packages).Profile!).SequenceEqual(originalPins), "Undo restores original exact pins");
        Check(!File.Exists(Path.Combine(after.Target,Catalog.Mod("mods-core").Module)), "Undo removes new loaders");
        manager.Apply(manager.Prepare("Disable","schematic-tools"));
        manager.Apply(manager.PrepareSchematicToolsMigration(packages));
        Check(Manager.SchematicToolsReplacementIds.All(id=>!manager.Inspect(packages).Profile!.Mods.Single(m=>m.Id==id).Enabled), "Disabled bundle migrates to disabled replacements");
        // The 0.17 diagnostic-only successor has no legacy conflict.
        Check(Version.Parse(debug.Manifest.Version)>=new Version(0,17,0), "Developer successor fixture present");
        Requirements.Validate(Manager.SchematicToolsReplacementIds.Select(id=>Package(id).Manifest).Append(debug.Manifest));
        checks.Add("Developer-only Schematic Tools can coexist with all replacements");
        File.WriteAllBytes(Path.Combine(output,"results.json"),Data.Bytes(checks)); Console.WriteLine(checks.Count+" dependency and split migration checks passed.");
    }
}
