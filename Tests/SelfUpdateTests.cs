using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using QdsModManager;

internal static class SelfUpdateTests
{
    internal static void Legacy(string oldCore, string oldZip, string primaryZip, string bridgeZip, string output)
    {
        output = Path.GetFullPath(output); if (Directory.Exists(output)) throw new IOException("Use a fresh output directory."); Directory.CreateDirectory(output);
        using var primary = new ZipArchive(File.OpenRead(primaryZip));
        using var dll = primary.GetEntry(AppIdentity.Name + ".dll")!.Open(); using var buffer = new MemoryStream(); dll.CopyTo(buffer);
        string version = Version.Parse(ManagerArchive.VerifyAssembly(buffer.ToArray(), AppIdentity.Name, null)).ToString(3);
        var releases = new[] { primaryZip, bridgeZip }.Select(path => new ManagerRelease(version, "v" + version, $"https://github.com/{ManagerRelease.Repository}/releases/download/v{version}/" + Path.GetFileName(path), Data.HashFile(path), new FileInfo(path).Length)).ToArray();
        byte[] metadata = Data.Bytes(new { draft = false, prerelease = false, tag_name = "v" + version, assets = releases.Select(r => new { name = new Uri(r.Url).Segments.Last(), browser_download_url = r.Url, size = r.Size, digest = "sha256:" + r.Sha256 }).ToArray() });
        if (ManagerRelease.Parse(metadata) != releases[0] || ManagerRelease.Parse(metadata, releases[1].Url) != releases[1]) throw new Exception("New client must prefer primary and verify either exact asset.");
        var context = new System.Runtime.Loader.AssemblyLoadContext("legacy-update-compatibility", true);
        try
        {
            var core = context.LoadFromAssemblyPath(Path.GetFullPath(oldCore));
            var releaseType = core.GetType("QdsModManager.ManagerRelease", true)!;
            var parse = releaseType.GetMethod("Parse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var legacyRelease = parse.Invoke(null, new object[] { metadata })!;
            if ((string)releaseType.GetProperty("Url")!.GetValue(legacyRelease)! != releases[1].Url) throw new Exception("Legacy client did not select compatibility asset.");
            // Only extract the fixed, known source layout into a fresh fixture.
            using (var zip = new ZipArchive(File.OpenRead(oldZip))) foreach (string name in AppIdentity.Files(AppIdentity.LegacyName))
            { using var entry = zip.GetEntry(name)!.Open(); using var file = File.Create(Path.Combine(output, name)); entry.CopyTo(file); }
            var updaterType = core.GetType("QdsModManager.SelfUpdater", true)!; var engine = Activator.CreateInstance(updaterType)!;
            var plan = updaterType.GetMethod("Prepare")!.Invoke(engine, new object[] { output, legacyRelease })!;
            var result = updaterType.GetMethod("Apply")!.Invoke(engine, new object[] { plan, File.ReadAllBytes(bridgeZip) })!;
            if (!(bool)result.GetType().GetProperty("Success")!.GetValue(result)!) throw new Exception("Old updater rejected compatibility release.");
            var bridge = ManagerArchive.Open(File.ReadAllBytes(bridgeZip), releases[1]);
            if (!bridge.Files.All(p => Data.HashFile(Path.Combine(output, p.Key)) == Data.Hash(p.Value))) throw new Exception("Legacy update bytes did not match.");
            File.WriteAllBytes(Path.Combine(output, "compatibility-result.json"), Data.Bytes(new { passed = true, legacySelectedBridge = true, newSelectedPrimary = true, legacyUpdaterInstalledBridge = true, version }));
            Console.WriteLine("Existing updater accepted and installed the compatibility release; new client selects the renamed release.");
        }
        finally { context.Unload(); }
    }
    internal static async Task Run(string oldZip, string newZip, string output)
    {
        output = Path.GetFullPath(output); if (Directory.Exists(output)) throw new IOException("Use a fresh test output directory."); Directory.CreateDirectory(output);
        byte[] oldBytes = File.ReadAllBytes(oldZip), newBytes = File.ReadAllBytes(newZip);
        ManagerRelease Release(string version, byte[] bytes) => new(version, "v" + version, $"https://github.com/{ManagerRelease.Repository}/releases/download/v{version}/{(version == "0.2.0" ? "QDS" : "Q-SYS")}-Mod-Manager-{version}-win-x64.zip", Data.Hash(bytes), bytes.Length);
        string candidateVersion;
        using (var zip = new ZipArchive(new MemoryStream(newBytes)))
        { string stem = zip.GetEntry(AppIdentity.Name + ".dll") is not null ? AppIdentity.Name : AppIdentity.LegacyName; using var entry = zip.GetEntry(stem + ".dll")!.Open(); using var assembly = new MemoryStream(); entry.CopyTo(assembly); candidateVersion = Version.Parse(ManagerArchive.VerifyAssembly(assembly.ToArray(), stem, null)).ToString(3); }
        var oldRelease = Release("0.2.0", oldBytes); var release = Release(candidateVersion, newBytes);
        var oldArchive = ManagerArchive.Open(oldBytes, oldRelease); var archive = ManagerArchive.Open(newBytes, release); var checks = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }
        void Reject(Action action, string label) { try { action(); } catch { checks.Add(label); return; } throw new Exception("Accepted: " + label); }
        async Task RejectAsync(Func<Task> action, string label) { try { await action(); } catch { checks.Add(label); return; } throw new Exception("Accepted: " + label); }
        var updater = new SelfUpdater(_ => false);
        string Fresh(string name)
        {
            string root = Path.Combine(output, name); Directory.CreateDirectory(root);
            foreach (var file in oldArchive.Files) File.WriteAllBytes(Path.Combine(root, file.Key), file.Value);
            Directory.CreateDirectory(Path.Combine(root, "Packages")); File.WriteAllText(Path.Combine(root, "Packages", "custom.qdsmod"), "keep package");
            File.WriteAllText(Path.Combine(root, "settings.json"), "keep settings"); File.WriteAllText(Path.Combine(root, "unrelated.dll"), "keep unrelated"); return root;
        }
        void Preserved(string root) => Check(File.ReadAllText(Path.Combine(root, "Packages", "custom.qdsmod")) == "keep package" && File.ReadAllText(Path.Combine(root, "settings.json")) == "keep settings" && File.ReadAllText(Path.Combine(root, "unrelated.dll")) == "keep unrelated", "Packages, settings and unknown files preserved: " + Path.GetFileName(root));
        Check(release.IsNewerThan("0.2.0.0") && !release.IsNewerThan(candidateVersion + ".0") && !release.IsNewerThan("99.0.0"), "Version comparison normalizes assembly revisions and avoids downgrade");
        string happy = Fresh("success"); var plan = updater.Prepare(happy, release); var result = updater.Apply(plan, newBytes);
        Check(result.Success && result.SafeToStart, "Portable manager update succeeds");
        Check(archive.Files.All(p => Data.HashFile(Path.Combine(happy, p.Key)) == Data.Hash(p.Value)), "All installed files match published archive");
        Check(ManagerArchive.KnownNames.Except(archive.Files.Keys).All(n => !File.Exists(Path.Combine(happy, n))), "Obsolete app names removed during migration");
        Check(AppIdentity.StemAt(happy) == (archive.Files.ContainsKey(AppIdentity.Name + ".exe") ? AppIdentity.Name : AppIdentity.LegacyName), "Renamed or compatibility executable is detected correctly");
        Check(Data.Read<AppFiles>(File.ReadAllBytes(Path.Combine(happy, "installed-files.json"))).Version == candidateVersion, "Installed version recorded");
        Check(plan.Before.Where(p => p.Value is not null).All(p => Data.HashFile(Path.Combine(result.Backup!, "before", p.Key)) == p.Value), "Every original file backed up with exact hash");
        Check(updater.Recover(plan).Success, "Recovery recognizes completed transaction"); Preserved(happy);
        Reject(() => updater.Prepare(happy, release), "Same version rejected"); Reject(() => updater.Prepare(happy, oldRelease), "Downgrade rejected");
        string legacy = Fresh("legacy");
        var legacyFiles = oldArchive.Files.Select(p => new AppFile(p.Key, Data.Hash(p.Value))).Append(new AppFile("Packages/custom.qdsmod", Data.HashFile(Path.Combine(legacy, "Packages", "custom.qdsmod")))).ToArray();
        File.WriteAllBytes(Path.Combine(legacy, "installed-files.json"), Data.Bytes(new AppFiles(1, "0.2.0", legacyFiles)));
        Check(updater.Apply(updater.Prepare(legacy, release), newBytes).Success, "Existing installed manager updates");
        Check(Data.Read<AppFiles>(File.ReadAllBytes(Path.Combine(legacy, "installed-files.json"))).Files.Last() == legacyFiles.Last(), "Bundled package records preserved"); Preserved(legacy);
        string stale = Fresh("stale"); var stalePlan = updater.Prepare(stale, release); File.WriteAllText(Path.Combine(stale, "README.md"), "external edit");
        Reject(() => updater.Apply(stalePlan, newBytes), "Changed installation rejected before replacement"); Check(File.ReadAllText(Path.Combine(stale, "README.md")) == "external edit", "External edit remains intact");
        string running = Fresh("running"); var runningPlan = updater.Prepare(running, release);
        Reject(() => new SelfUpdater(_ => true).Apply(runningPlan, newBytes), "Running manager blocks replacement"); Check(SelfUpdater.OriginalIntact(runningPlan), "Running-manager rejection changes nothing");
        string locked = Fresh("locked"); var lockedPlan = updater.Prepare(locked, release); Directory.CreateDirectory(Path.Combine(locked, ".manager-updates"));
        using (var file = new FileStream(Path.Combine(locked, ".manager-updates", "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            Reject(() => updater.Apply(lockedPlan, newBytes), "Concurrent update lock prevents replacement");
        string wrong = Fresh("wrong-identity"); File.Copy(Path.Combine(wrong, "QdsModManager.Core.dll"), Path.Combine(wrong, "QDS Mod Manager.dll"), true);
        Reject(() => updater.Prepare(wrong, release), "Unexpected installed assembly identity rejected");
        string link = Fresh("linked"); string readme = Path.Combine(link, "README.md"); File.Move(readme, readme + ".original"); File.CreateSymbolicLink(readme, readme + ".original");
        Reject(() => updater.Prepare(link, release), "Linked target file rejected");
        for (int failure = 1; failure <= plan.Before.Count; failure++)
        {
            string root = Fresh("rollback-" + failure); var rollbackPlan = updater.Prepare(root, release); int at = failure;
            var failing = new SelfUpdater(_ => false) { AfterWrite = n => { if (n == at) throw new IOException("Injected write failure"); } };
            var rollback = failing.Apply(rollbackPlan, newBytes);
            Check(!rollback.Success && rollback.SafeToStart && SelfUpdater.OriginalIntact(rollbackPlan), "Failure after write " + failure + " restores exact previous installation");
            Check(updater.Recover(rollbackPlan).SafeToStart, "Rolled-back transaction can be inspected: " + failure); Preserved(root);
        }
        async Task<SelfUpdatePlan> Interrupted(string name)
        {
            string root = Fresh(name); var interrupted = updater.Prepare(root, release); string request = Path.Combine(root, "test-plan.json"); File.WriteAllBytes(request, Data.Bytes(interrupted));
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(typeof(SelfUpdateTests).Assembly.Location);
            start.ArgumentList.Add("--self-update-crash"); start.ArgumentList.Add(request); start.ArgumentList.Add(Path.GetFullPath(newZip));
            using var child = Process.Start(start)!; await child.WaitForExitAsync(); Check(child.ExitCode == 73, "Actual updater process interruption: " + name); return interrupted;
        }
        var crash = await Interrupted("crash"); Reject(() => updater.Prepare(crash.Target, release), "Pending update blocks new transaction");
        Check(updater.Recover(crash).SafeToStart && SelfUpdater.OriginalIntact(crash), "Recovery after terminated updater restores exact original files"); Preserved(crash.Target);
        var corrupt = await Interrupted("corrupt-backup"); string backup = Path.Combine(corrupt.Target, ".manager-updates", corrupt.Id, "before", "README.md"); File.WriteAllText(backup, "broken backup");
        string[] tracked = ManagerArchive.KnownNames.Append("installed-files.json").ToArray();
        string[] Snapshot(string root) => tracked.Select(n => File.Exists(Path.Combine(root, n)) ? Data.HashFile(Path.Combine(root, n)) : "absent").ToArray();
        var corruptBefore = Snapshot(corrupt.Target); Reject(() => updater.Recover(corrupt), "Damaged backup stops recovery"); Check(corruptBefore.SequenceEqual(Snapshot(corrupt.Target)), "All backups validated before any recovery write");
        var edited = await Interrupted("edited-during-recovery"); File.WriteAllText(Path.Combine(edited.Target, "README.md"), "owner change"); var editedBefore = Snapshot(edited.Target);
        Reject(() => updater.Recover(edited), "Recovery rejects external file changes"); Check(editedBefore.SequenceEqual(Snapshot(edited.Target)), "Recovery leaves external changes and other files intact");
        string noWrite = Fresh("no-write"); Check(updater.Recover(updater.Prepare(noWrite, release)).SafeToStart, "Recovery before first write keeps old manager usable");
        byte[] damaged = newBytes.ToArray(); damaged[^1] ^= 1; Reject(() => ManagerArchive.Open(damaged, release), "Modified download rejected by digest");
        Reject(() => ManagerArchive.Open(newBytes, release with { Size = release.Size + 1 }), "Wrong download size rejected");
        Reject(() => ManagerArchive.Open(newBytes, Release("99.0.0", newBytes)), "Archive assembly version must match release");
        Reject(() => (release with { Url = "https://example.com/update.zip" }).Validate(), "Non-project download rejected");
        Reject(() => (release with { Tag = "v0.3.0-beta" }).Validate(), "Prerelease tag rejected");
        byte[] Repack(Action<ZipArchive> edit)
        {
            using var stream = new MemoryStream(); using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            { foreach (var file in archive.Files) { using var entry = zip.CreateEntry(file.Key).Open(); entry.Write(file.Value); } edit(zip); } return stream.ToArray();
        }
        foreach (string entry in new[] { "../outside.dll", "QDS Mod Manager.dll", "extra.exe" })
        { byte[] altered = Repack(zip => { using var extra = zip.CreateEntry(entry).Open(); extra.WriteByte(1); }); Reject(() => ManagerArchive.Open(altered, Release(candidateVersion, altered)), "Unexpected or duplicate archive entry rejected: " + entry); }
        byte[] Api(ManagerRelease item, bool pre = false, string? digest = "auto") => Data.Bytes(new { draft = false, prerelease = pre, tag_name = item.Tag, assets = new[] { new { name = new Uri(item.Url).Segments.Last(), browser_download_url = item.Url, size = item.Size, digest = digest == "auto" ? "sha256:" + item.Sha256 : digest } } });
        using (var client = new ManagerReleaseClient(new HttpClient(new Handler(req =>
        {
            Check(req.RequestUri!.ToString() == ManagerReleaseClient.Api + "latest" || req.RequestUri.ToString() == ManagerReleaseClient.Api + "tags/" + release.Tag || req.RequestUri.ToString() == release.Url, "Only expected release endpoints requested");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(req.RequestUri.ToString() == release.Url ? newBytes : Api(release)) };
        }))))
        {
            Check(await client.Latest(default) == release, "Stable release metadata read"); await client.VerifyPublished(release, default); Check((await client.Download(release, default)).SequenceEqual(newBytes), "Verified update download succeeds");
        }
        Reject(() => ManagerRelease.Parse(Api(release, true)), "Prerelease metadata rejected"); Reject(() => ManagerRelease.Parse(Api(release, digest: null)), "Missing GitHub checksum rejected");
        using (var client = new ManagerReleaseClient(new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Api(release with { Sha256 = new string('a', 64) })) }))))
            await RejectAsync(() => client.VerifyPublished(release, default), "Elevated installer rejects modified release metadata");
        foreach (var code in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests })
        { using var client = new ManagerReleaseClient(new HttpClient(new Handler(_ => new(code)))); await RejectAsync(() => client.Latest(default), "HTTP error is not reported as up to date: " + code); }
        using (var client = new ManagerReleaseClient(new HttpClient(new Handler(_ => { var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://example.com/redirect"); return response; }))))
            await RejectAsync(() => client.Download(release, default), "Download cannot redirect outside release hosts");
        using (var client = new ManagerReleaseClient(new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[600000]) }))))
            await RejectAsync(() => client.Latest(default), "Oversized release metadata rejected");
        using (var client = new ManagerReleaseClient(new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(newBytes) }))))
            await RejectAsync(() => client.Latest(new CancellationToken(true)), "Canceled update check stops");
        File.WriteAllBytes(Path.Combine(output, "result.json"), Data.Bytes(new { status = "passed", checks })); Console.WriteLine($"{checks.Count} self-update checks passed.");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(send(request)); } }
}
