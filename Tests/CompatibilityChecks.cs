using QdsModManager;

internal static class CompatibilityChecks
{
    internal static void Run(string output, ModPackage package, byte[] host, byte[] common, List<string> checks)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        void Reject(Action action, string label)
        {
            try { action(); } catch (Exception) { checks.Add(label); return; }
            throw new Exception("Accepted: " + label);
        }
        string Fresh(string name, bool tested = false)
        {
            string root = Path.Combine(output, name); Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "Q-Sys Designer.dll"), host);
            File.WriteAllBytes(Path.Combine(root, "ModuleCommon.dll"), tested ? common : [1, 2, 3]);
            File.WriteAllText(Path.Combine(root, "Designer.Modules.Unrelated.dll"), "preserve"); return root;
        }
        Manager Engine(string root, bool running = false) => new(root, Data.Hash(host), Data.Hash(common), _ => running);
        OperationPlan Confirm(OperationPlan plan) => Data.Read<OperationPlan>(Data.Bytes(plan with { UntestedVersionAcknowledged = true }));
        Dictionary<string, string> Files(string root) => Directory.GetFiles(root).ToDictionary(path => Path.GetFileName(path)!, Data.HashFile);
        void Same(Dictionary<string, string> before, string root, string label) => Check(before.OrderBy(x => x.Key).SequenceEqual(Files(root).OrderBy(x => x.Key)), label);

        string root = Fresh("untested-version"); var manager = Engine(root); var before = Files(root);
        var inspection = manager.Inspect([package]);
        Check(inspection.Build is { Tested: false } && inspection.Build.CommonSha256 == Data.HashFile(Path.Combine(root, "ModuleCommon.dll")), "Untested installations are inspectable with their actual build hashes");
        Check(manager.CachedPackages().Length == 0, "Untested installations can load their package library");
        var plan = manager.Prepare("Install", package.Manifest.Id, package.Path);
        Check(plan.ExpectedBuild == inspection.Build && !plan.UntestedVersionAcknowledged, "Untested preview records the build without preconfirming the warning");
        Same(before, root, "Canceling an untested preview preserves installation files");
        Reject(() => manager.Apply(plan), "Worker rejects an unacknowledged untested preview");
        Reject(() => manager.Apply(Confirm(plan) with { ExpectedBuild = null }), "Untested consent requires the previewed build identity");
        Reject(() => manager.Apply(Confirm(plan) with { ExpectedBuild = plan.ExpectedBuild! with { Tested = true } }), "Caller cannot claim an untested build is tested");
        Check(!Directory.Exists(Path.Combine(root, ".qds-mod-manager")), "Unacknowledged attempts do not create manager state or locks");
        Check(manager.Apply(Confirm(plan)).Success, "Serialized acknowledgement allows untested installation");
        Check(manager.Inspect([package]).Mods.Single(m => m.Id == package.Manifest.Id).Enabled, "Untested installation reports the enabled mod");
        foreach (string action in new[] { "Disable", "Enable", "Remove", "Undo" })
            Check(manager.Apply(Confirm(manager.Prepare(action, package.Manifest.Id))).Success, action + " works on an acknowledged untested build");
        Check(Data.HashFile(Path.Combine(root, "Q-Sys Designer.dll")) == before["Q-Sys Designer.dll"] && Data.HashFile(Path.Combine(root, "ModuleCommon.dll")) == before["ModuleCommon.dll"], "Untested operations preserve vendor binaries");
        Check(File.ReadAllText(Path.Combine(root, "Designer.Modules.Unrelated.dll")) == "preserve", "Untested operations preserve unrelated modules");

        var profile = manager.Inspect([package]).Profile!;
        var suspend = manager.PrepareProfile(profile, [package], true, "Suspend");
        Reject(() => manager.Apply(suspend), "Grouped untested operations also require acknowledgement");
        Check(manager.Apply(Confirm(suspend)).Success && manager.Inspect([package]).Suspended, "Untested profile suspension succeeds");
        Check(manager.Apply(Confirm(manager.PrepareProfile(profile, [package], false, "Resume"))).Success, "Untested profile resume succeeds");
        Reject(() => Engine(root, true).Apply(Confirm(manager.Prepare("Disable", package.Manifest.Id))), "Untested warning cannot bypass running Designer protection");

        foreach (bool tested in new[] { true, false })
        {
            string changed = Fresh("build-changed-" + tested, tested); var engine = Engine(changed);
            var stale = Confirm(engine.Prepare("Install", package.Manifest.Id, package.Path));
            File.WriteAllText(Path.Combine(changed, "Q-Sys Designer.dll"), "another build"); var changedBefore = Files(changed);
            Reject(() => engine.Apply(stale), "Changed " + (tested ? "tested" : "untested") + " build invalidates confirmed preview");
            Same(changedBefore, changed, "Stale build rejection preserves all files");
            Check(!Directory.Exists(Path.Combine(changed, ".qds-mod-manager")), "Stale build rejection creates no manager state");
        }
        string failing = Fresh("untested-rollback"); var failed = Engine(failing); var failedBefore = Files(failing);
        failed.AfterWrite = _ => throw new IOException("Injected failure");
        Reject(() => failed.Apply(Confirm(failed.Prepare("Install", package.Manifest.Id, package.Path))), "Untested transaction failures surface");
        Same(failedBefore, failing, "Untested transaction rolls back all installation files");
        string midChange = Fresh("build-changed-during-apply"); var midManager = Engine(midChange); var midBefore = Files(midChange);
        midManager.AfterWrite = _ => File.WriteAllText(Path.Combine(midChange, "ModuleCommon.dll"), "external host update");
        Reject(() => midManager.Apply(Confirm(midManager.Prepare("Install", package.Manifest.Id, package.Path))), "Host change during apply stops the transaction");
        midBefore["ModuleCommon.dll"] = Data.HashFile(Path.Combine(midChange, "ModuleCommon.dll"));
        Same(midBefore, midChange, "Host-change rollback restores mod files and preserves the external host update");
        string interrupted = Fresh("untested-recovery"); var recover = Engine(interrupted); var recoverBefore = Files(interrupted);
        recover.Apply(Confirm(recover.Prepare("Install", package.Manifest.Id, package.Path)));
        string journalPath = Directory.GetFiles(Path.Combine(interrupted, ".qds-mod-manager", "history"), "*.json").Single();
        var journal = Data.Read<Journal>(File.ReadAllBytes(journalPath));
        File.WriteAllBytes(journalPath, Data.Bytes(journal with { Status = "Pending" }));
        Check(recover.Inspect([package]).Pending is not null, "Untested interrupted state remains visible");
        var recovery = recover.Prepare("Recover"); Reject(() => recover.Apply(recovery), "Untested recovery preview also requires acknowledgement");
        Check(recover.Apply(Confirm(recovery)).Success, "Acknowledged untested recovery succeeds");
        Same(recoverBefore, interrupted, "Untested recovery restores original installation files");

        foreach (string name in new[] { "Q-Sys Designer.dll", "ModuleCommon.dll" })
        {
            string incomplete = Fresh("missing-" + name); File.Delete(Path.Combine(incomplete, name));
            Reject(() => Engine(incomplete).Inspect([]), "Missing " + name + " still blocks inspection");
            File.WriteAllBytes(Path.Combine(incomplete, name), []);
            Reject(() => Engine(incomplete).Prepare("Install", package.Manifest.Id, package.Path), "Empty " + name + " still blocks preparation");
        }
        string testedRoot = Fresh("tested-version", true); var testedManager = Engine(testedRoot);
        Check(testedManager.Inspect([]).Build?.Tested is true, "Exact tested build is recognized without warning");
        Check(testedManager.Apply(testedManager.Prepare("Install", package.Manifest.Id, package.Path)).Success, "Tested build still installs without untested acknowledgement");
    }
}
