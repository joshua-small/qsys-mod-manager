using QdsModManager;

// Optional native file-transaction acceptance. Source vendor files are read only;
// all writes occur in newly created disposable folders, and no assembly is run.
internal static class ExactHostChecks
{
    internal static void Run(string source, string packagePath, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Use a fresh output directory.");
        byte[] host = File.ReadAllBytes(Path.Combine(source, "Q-Sys Designer.dll")), common = File.ReadAllBytes(Path.Combine(source, "ModuleCommon.dll"));
        if (Data.Hash(host) != PackageCompatibility.Host1051 || Data.Hash(common) != PackageCompatibility.Common1051) throw new InvalidDataException("Expected the exact 10.5.1 source pair.");
        Directory.CreateDirectory(output); var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        void Reject(Action action, string label) { try { action(); } catch { checks.Add(label); return; } throw new Exception("Accepted: " + label); }
        var package = ExactPackageChecks.Package(Path.Combine(output, "synthetic-exact.qdsmod"), ModPackage.Open(packagePath));
        string module = Catalog.Mod(package.Manifest.Id).Module;
        string Fresh(string name) { string path = Path.Combine(output, name); Directory.CreateDirectory(path); File.WriteAllBytes(Path.Combine(path, "Q-Sys Designer.dll"), host); File.WriteAllBytes(Path.Combine(path, "ModuleCommon.dll"), common); File.WriteAllText(Path.Combine(path, "user.txt"), "preserve"); return path; }
        OperationPlan Confirm(OperationPlan plan) => Data.Read<OperationPlan>(Data.Bytes(plan with { UntestedVersionAcknowledged = true }));
        string root = Fresh("transactions"); var engine = new Manager(root);
        var plan = engine.PrepareInstall(package, [package]);
        Check(plan.ExpectedBuild is { Tested: false } && plan.Group!.Profile.DesignerVersion == "10.5.1", "Actual 10.5.1 hashes prepare exact profile without global tested claim");
        Reject(() => engine.Apply(plan), "Exact package match still requires untested host consent");
        Check(!Directory.Exists(Path.Combine(root, ".qds-mod-manager")), "Rejected consent leaves target state untouched");
        Check(engine.Apply(Confirm(plan)).Success && Data.HashFile(Path.Combine(root, module)) == package.Manifest.Files.Single(f => f.Name == module).Sha256, "Confirmed exact-target grouped install writes verified payload");
        Check(engine.Inspect([package]).Profile!.HostSha256 == PackageCompatibility.Host1051, "Applied profile retains exact package target");
        foreach (string action in new[] { "Disable", "Enable" }) Check(engine.Apply(Confirm(engine.Prepare(action, package.Manifest.Id))).Success, "Exact package " + action + " succeeds on matching target");
        var profile = engine.Inspect([package]).Profile!;
        Check(engine.Apply(Confirm(engine.PrepareProfile(profile, [package], true, "Suspend"))).Success && !File.Exists(Path.Combine(root, module)), "Exact profile suspension removes module");
        Check(engine.Apply(Confirm(engine.PrepareProfile(profile, [package], false, "Resume"))).Success && File.Exists(Path.Combine(root, module)), "Exact profile resume restores verified module");
        var blocked = new Manager(root, Catalog.HostHash, Catalog.CommonHash, _ => true);
        Reject(() => blocked.Apply(Confirm(blocked.Prepare("Disable", package.Manifest.Id))), "Exact package cannot bypass running-Designer guard");
        engine.Apply(Confirm(engine.Prepare("Disable", package.Manifest.Id)));
        File.WriteAllText(Path.Combine(root, "ModuleCommon.dll"), "different host");
        Reject(() => engine.Prepare("Undo"), "Explicit Undo cannot reactivate 10.5.1 package on a changed host");
        Check(!File.Exists(Path.Combine(root, module)), "Rejected incompatible Undo preserves disabled state");
        File.WriteAllBytes(Path.Combine(root, "ModuleCommon.dll"), common);
        Check(engine.Apply(Confirm(engine.Prepare("Undo"))).Success, "Undo restores exact package after correct host is restored");
        var stale = Confirm(engine.Prepare("Disable", package.Manifest.Id));
        File.WriteAllText(Path.Combine(root, "ModuleCommon.dll"), "another host");
        Check(engine.Inspect([package]).Mods.Single(m => m.Id == package.Manifest.Id).Status == "Requires Designer 10.5.1", "Installed exact package reports incompatible target after a host change");
        Reject(() => engine.Apply(stale), "Host change invalidates exact-package preview");
        File.WriteAllBytes(Path.Combine(root, "ModuleCommon.dll"), common);
        engine.AfterWrite = _ => throw new IOException("Injected exact-package failure");
        Reject(() => engine.Apply(Confirm(engine.Prepare("Disable", package.Manifest.Id))), "Exact-package failure surfaces"); engine.AfterWrite = null;
        Check(File.Exists(Path.Combine(root, module)) && engine.Inspect([package]).Pending is null && engine.Inspect([package]).Mods.Single(m => m.Id == package.Manifest.Id).Enabled, "Failure rolls back module and exact profile together");
        engine.Apply(Confirm(engine.Prepare("Disable", package.Manifest.Id)));
        string journalPath = Directory.GetFiles(Path.Combine(root, ".qds-mod-manager", "history")).OrderDescending().First();
        var journal = Data.Read<Journal>(File.ReadAllBytes(journalPath)); File.WriteAllBytes(journalPath, Data.Bytes(journal with { Status = "Pending" }));
        File.WriteAllText(Path.Combine(root, "ModuleCommon.dll"), "changed before recovery");
        Reject(() => engine.Apply(Confirm(engine.Prepare("Recover"))), "Explicit recovery cannot restore enabled exact package on wrong host");
        Check(!File.Exists(Path.Combine(root, module)), "Rejected recovery preserves disabled files and backups");
        File.WriteAllBytes(Path.Combine(root, "ModuleCommon.dll"), common);
        Check(engine.Apply(Confirm(engine.Prepare("Recover"))).Success && File.Exists(Path.Combine(root, module)), "Exact-build recovery succeeds after host is restored");
        Check(Data.HashFile(Path.Combine(root, "Q-Sys Designer.dll")) == PackageCompatibility.Host1051 && Data.HashFile(Path.Combine(root, "ModuleCommon.dll")) == PackageCompatibility.Common1051 && File.ReadAllText(Path.Combine(root, "user.txt")) == "preserve", "Native transactions preserve vendor and user bytes");
        File.WriteAllBytes(Path.Combine(output, "checks.json"), Data.Bytes(checks)); Console.WriteLine(checks.Count + " exact-host file transaction checks passed; no Designer runtime acceptance claimed.");
    }
}
