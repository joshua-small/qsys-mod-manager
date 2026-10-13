using System.IO.Compression;
using System.Security.Cryptography;
using QdsModManager;

internal static class ApprovalChecks
{
    internal static void Run(string output, ModPackage legacy, ModPackage geometry, byte[] host, byte[] common, List<string> checks)
    {
        var original = Catalog.Approvals;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        void Reject(Action action, string label) { try { action(); } catch { checks.Add(label); return; } throw new Exception("Accepted: " + label); }
        string Fresh(string name)
        {
            string root = Path.Combine(output, name); Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "Q-Sys Designer.dll"), host); File.WriteAllBytes(Path.Combine(root, "ModuleCommon.dll"), common);
            return root;
        }
        Manager Engine(string root) => new(root, Data.Hash(host), Data.Hash(common), _ => false);
        ModPackage Package(string name, PackageManifest manifest, IReadOnlyDictionary<string, byte[]> files)
        {
            string path = Path.Combine(output, name + ".qdsmod");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var stream = zip.CreateEntry("manifest.json").Open()) stream.Write(Data.Bytes(manifest));
                foreach (var file in manifest.Files) using (var stream = zip.CreateEntry("payload/" + file.Name).Open()) stream.Write(files[file.Name]);
            }
            return ModPackage.Open(path);
        }
        ModPackage Modern(ModPackage package) => Package(package.Manifest.Id + "-markerless", package.Manifest with
        {
            Schema = 2, Files = package.Manifest.Files.Where(f => f.Name != Catalog.Mod(package.Manifest.Id).Marker).ToArray()
        }, package.Files);
        var modern = Modern(legacy); var modernGeometry = Modern(geometry);
        Check(modern.Manifest.Files.Length == 3 && modernGeometry.Manifest.Files.Length == 3, "Modern packages contain no activation marker");
        var root = Fresh("marker-migration"); var manager = Engine(root);
        manager.Apply(manager.Prepare("Install", legacy.Manifest.Id, legacy.Path));
        string marker = Path.Combine(root, Catalog.Mod(legacy.Manifest.Id).Marker);
        var preview = manager.Prepare("Install", legacy.Manifest.Id, modern.Path);
        Check(File.Exists(marker), "Preview does not remove the legacy marker");
        manager.Apply(preview);
        Check(!File.Exists(marker) && manager.Inspect([modern]).Mods.Single(m => m.Id == legacy.Manifest.Id).Enabled, "Upgrade removes the managed legacy marker and remains enabled");
        manager.Apply(manager.Prepare("Undo"));
        Check(File.Exists(marker), "Whole-change undo restores the legacy marker");
        manager.AfterWrite = _ => throw new IOException("Migration failure");
        Reject(() => manager.Apply(manager.Prepare("Install", legacy.Manifest.Id, modern.Path)), "Marker migration failure reported");
        manager.AfterWrite = null;
        Check(File.Exists(marker) && manager.Inspect([legacy]).Pending is null, "Marker migration failure rolls back files and state");
        manager.Apply(manager.Prepare("Install", geometry.Manifest.Id, geometry.Path));
        var profile = ModProfile.Create("Modern set", new[] {
            new InstalledMod(modern.Manifest.Id, modern.Manifest.Version, modern.ArchiveHash, true),
            new InstalledMod(modernGeometry.Manifest.Id, modernGeometry.Manifest.Version, modernGeometry.ArchiveHash, false)
        });
        manager.Apply(manager.PrepareProfile(profile, [modern, modernGeometry]));
        Check(!File.Exists(marker) && !File.Exists(Path.Combine(root, Catalog.Mod(geometry.Manifest.Id).Marker)), "Grouped migration removes legacy markers for enabled and disabled mods");
        manager.Apply(manager.PrepareProfile(profile, [modern, modernGeometry], true, "Suspend"));
        Check(!File.Exists(Path.Combine(root, Catalog.Mod(legacy.Manifest.Id).Module)), "Suspension removes the managed markerless module");
        manager.Apply(manager.PrepareProfile(profile, [modern, modernGeometry], false, "Resume"));
        Check(File.Exists(Path.Combine(root, Catalog.Mod(legacy.Manifest.Id).Module)) && !File.Exists(marker), "Resume restores module without creating a marker");
        manager.Apply(manager.Prepare("Disable", legacy.Manifest.Id));
        Check(!File.Exists(Path.Combine(root, Catalog.Mod(legacy.Manifest.Id).Module)), "Individual disable removes a markerless module");
        manager.Apply(manager.Prepare("Enable", legacy.Manifest.Id));
        Check(!File.Exists(marker), "Individual enable does not create a marker");

        var definition = new ModDefinition("community-example", "Community Example", "community/example", "Designer.Modules.CommunityExample.dll", null,
            [new(2, [new("Designer.Modules.CommunityExample.dll", false)])]);
        var expanded = new ApprovalDocument(1, original.Mods.Append(definition).ToArray());
        var proof = Data.Read<SignedCatalog>(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "catalog-signed.json")));
        Check(CatalogTrust.Verify(proof).Approvals.Mods.Length == 2, "Bundled public key verifies the real signed catalog fixture");
        var alteredFeed = CatalogTrust.Verify(proof) with { Approvals = expanded };
        Reject(() => Catalog.Activate(proof with { Payload = Convert.ToBase64String(Data.Bytes(alteredFeed)) }), "Editing cached approval data invalidates the signature");
        using (var attacker = ECDsa.Create())
        {
            var bytes = Data.Bytes(alteredFeed);
            Reject(() => Catalog.Activate(new(1, Convert.ToBase64String(bytes), Convert.ToBase64String(attacker.SignData(bytes, HashAlgorithmName.SHA256)))), "Self-signed community approvals rejected");
        }
        Reject(() => ApprovalDocument.Preserve(original, new(1, [original.Mods[0]])), "Removing an approval needed by historical profiles rejected");
        Reject(() => ApprovalDocument.Validate(expanded with { Mods = original.Mods.Append(definition with { Module = "Q-Sys Designer.dll" }).ToArray() }), "Catalog cannot approve vendor binary replacement");
        foreach (var file in new[] { "../escape.dll", "C:escape.dll", "Q-Sys Designer.exe", "ModuleCommon.dll", "Designer.Modules.SelectionProbe.dll" })
            Reject(() => ApprovalDocument.Validate(expanded with { Mods = original.Mods.Append(definition with { Layouts = [new(2, [new(definition.Module, false), new(file, false)])] }).ToArray() }), "Unsafe or foreign ownership rejected: " + file);
        Reject(() => ApprovalDocument.Validate(expanded with { Mods = original.Mods.Append(definition with { Layouts = [new(2, [new(definition.Module, false), new(Catalog.Harmony, true, new string('0', 64))])] }).ToArray() }), "Conflicting shared dependency hash rejected");
        // Reproduce the different dictionary ordering used by older managers' journals.
        var historyFile = Directory.GetFiles(Path.Combine(root, ".qds-mod-manager", "history")).OrderDescending().First();
        var historical = Data.Read<Journal>(File.ReadAllBytes(historyFile));
        var reorderedAfter = historical.After.Reverse().ToDictionary(p => p.Key, p => p.Value);
        historical = historical with { Schema = 1, FileSetSha256 = null, Before = historical.Before.Reverse().ToDictionary(p => p.Key, p => p.Value), After = reorderedAfter,
            AfterStamp = Data.Hash(Data.Bytes(new { files = reorderedAfter, state = historical.AfterState })) };
        File.WriteAllBytes(historyFile, Data.Bytes(historical));
        Check(manager.Inspect([modern]).CanUndo, "Historical journal ordering remains valid");
        var shared = ModProfile.Create("Shared across installations", [new(legacy.Manifest.Id, legacy.Manifest.Version, legacy.ArchiveHash, true)]);
        var first = Engine(Fresh("catalog-growth-first")); var second = Engine(Fresh("catalog-growth-second"));
        first.Apply(first.PrepareProfile(shared, [legacy])); second.Apply(second.PrepareProfile(shared, [legacy]));
        string libraryDirectory = Path.Combine(output, "catalog-growth-library");
        var library = new ProfileLibrary(libraryDirectory); library.Reconcile(first.Inspect([legacy])); library.Reconcile(second.Inspect([legacy]));
        first.Apply(first.Prepare("Install", legacy.Manifest.Id, modern.Path)); library.Reconcile(first.Inspect([modern]));
        var legacyStore = library.Store with { Schema = 1, Assignments = library.Store.Assignments.Select(a => a with
            { AppliedStamp = null, Stamp = (a.Target == first.Inspect([]).Target ? first : second).Inspect([]).LegacyStamp! }).ToArray() };
        File.WriteAllBytes(Path.Combine(libraryDirectory, "profiles.json"), Data.Bytes(legacyStore));
        ApprovalDocument.Preserve(original, expanded); Catalog.ForTool(expanded);
        library = new ProfileLibrary(libraryDirectory); library.Reconcile(second.Inspect([legacy]));
        Check(library.Store.Profiles.Single(p => p.Id == shared.Id).Mods.Single().Sha256 == modern.ArchiveHash, "Catalog growth and 0.6 library migration preserve another installation's newer saved pins");
        Check(library.Store.Schema == 2, "Profile assignment migration records stable applied-configuration stamps");
        // Adding a definition must not invalidate earlier journals or offline restoration.
        Check(manager.Inspect([modern]).CanUndo, "Catalog growth preserves whole-change undo");
        manager.Apply(manager.Prepare("Undo"));
        Check(!File.Exists(Path.Combine(root, Catalog.Mod(legacy.Manifest.Id).Module)), "Undo after catalog growth restores the earlier file set");
        var moduleBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, definition.Module));
        var manifest = new PackageManifest(2, definition.Id, definition.Name, "1.0.0.0", "Author-owned text", Catalog.HostHash, Catalog.CommonHash,
            [new(definition.Module, Data.Hash(moduleBytes), moduleBytes.Length, false)]);
        var community = Package("community", manifest, new Dictionary<string, byte[]> { [definition.Module] = moduleBytes });
        string communityRoot = Fresh("community-install"); var communityManager = Engine(communityRoot);
        communityManager.Apply(communityManager.Prepare("Install", definition.Id, community.Path));
        Check(communityManager.Inspect([community]).Mods.Single(m => m.Id == definition.Id).Enabled, "Previously unknown mod installs from approved data without a manager code change");
        var communityJournalPath = Directory.GetFiles(Path.Combine(communityRoot, ".qds-mod-manager", "history")).Single();
        byte[] intactJournal = File.ReadAllBytes(communityJournalPath);
        var truncated = Data.Read<Journal>(intactJournal);
        truncated.Before.Remove(Catalog.License); truncated.After.Remove(Catalog.License);
        File.WriteAllBytes(communityJournalPath, Data.Bytes(truncated));
        Reject(() => communityManager.Inspect([community]), "Incomplete modern journal rejected even after catalog growth");
        File.WriteAllBytes(communityJournalPath, intactJournal);
        Check(!File.Exists(Path.Combine(communityRoot, Catalog.Harmony)) && Directory.GetFiles(communityRoot, "*enabled*").Length == 0, "Community package requires neither Harmony nor markers");
        communityManager.Apply(communityManager.Prepare("Disable", definition.Id)); communityManager.Apply(communityManager.Prepare("Enable", definition.Id));
        communityManager.Apply(communityManager.Prepare("Remove", definition.Id)); communityManager.Apply(communityManager.Prepare("Undo"));
        Check(File.Exists(Path.Combine(communityRoot, definition.Module)), "Community mod supports disable, enable, removal and undo");
        var schematicDefinition = expanded.Mod(legacy.Manifest.Id);
        string extraName = "Qmm.schematic-tools.notes.txt"; byte[] extraBytes = System.Text.Encoding.UTF8.GetBytes("package-owned content");
        var futureLayout = new PackageLayout(2, schematicDefinition.Layouts.Last().Files.Append(new ApprovedFile(extraName, false)).ToArray());
        var extended = expanded with { Mods = expanded.Mods.Select(m => m.Id == schematicDefinition.Id ? m with { Layouts = m.Layouts.Append(futureLayout).ToArray() } : m).ToArray() };
        Catalog.ForTool(extended);
        library.Reconcile(second.Inspect([legacy]));
        Check(library.Store.Profiles.Single(p => p.Id == shared.Id).Mods.Single().Sha256 == modern.ArchiveHash, "Later file-layout additions do not overwrite saved profile pins");
        var futureFiles = modern.Files.ToDictionary(p => p.Key, p => p.Value); futureFiles[extraName] = extraBytes;
        var futurePackage = Package("extra-private-file", modern.Manifest with { Files = modern.Manifest.Files.Append(new PackageFile(extraName, Data.Hash(extraBytes), extraBytes.Length, false)).ToArray() }, futureFiles);
        File.WriteAllText(Path.Combine(root, extraName), "existing user file");
        Reject(() => manager.Prepare("Install", legacy.Manifest.Id, futurePackage.Path), "Layout expansion cannot overwrite an unmanaged private file");
        var futureProfile = ModProfile.Create("Future layout", [new(legacy.Manifest.Id, futurePackage.Manifest.Version, futurePackage.ArchiveHash, true)]);
        Reject(() => manager.PrepareProfile(futureProfile, [futurePackage]), "Grouped layout expansion preserves unmanaged private files");
        Check(File.ReadAllText(Path.Combine(root, extraName)) == "existing user file", "Unmanaged file remains unchanged after rejected upgrades");
        Catalog.ForTool(original);
        Reject(() => ModPackage.Open(community.Path), "Unapproved local community package rejected");
    }
}
