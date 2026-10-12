using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace QdsModManager.App;

internal sealed record UpdateRequest(SelfUpdatePlan Plan, int ParentId, long ParentStart);
internal sealed record UpdateNotice(string Target, SelfUpdateResult Result);

internal static class AppUpdate
{
    internal static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
    internal static string Updates => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QDS Mod Manager", "Updates");
    internal static async Task<string> Stage(ManagerRelease release, CancellationToken cancellation)
    {
        if (!string.Equals(Path.GetFileName(Environment.ProcessPath), "QDS Mod Manager.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Open QDS Mod Manager.exe to install an app update.");
        var plan = new SelfUpdater().Prepare(AppContext.BaseDirectory, release);
        using var client = new ManagerReleaseClient(); byte[] bytes = await client.Download(release, cancellation);
        return StageVerified(plan, bytes);
    }
    // Used by the native handoff test as well as the application. No installation occurs here.
    internal static string StageVerified(SelfUpdatePlan plan, byte[] bytes, Process? parent = null)
    {
        _ = ManagerArchive.Open(bytes, plan.Release); SelfUpdater.Guard(Updates); Directory.CreateDirectory(Updates);
        string folder = Path.Combine(Updates, plan.Id); SelfUpdater.Guard(folder);
        if (Directory.Exists(folder)) throw new IOException("This update has already been staged."); Directory.CreateDirectory(folder);
        string runner = Path.Combine(folder, "runner"); Directory.CreateDirectory(runner);
        foreach (var name in ManagerArchive.Names.Take(5))
        {
            // Run a copy of the current updater code so its files can be replaced.
            string source = Path.Combine(AppContext.BaseDirectory, name); SelfUpdater.Guard(source);
            File.WriteAllBytes(Path.Combine(runner, name), File.ReadAllBytes(source));
        }
        File.WriteAllBytes(Path.Combine(folder, "package.zip"), bytes);
        using var current = parent is null ? Process.GetCurrentProcess() : null; var process = parent ?? current!;
        WriteNew(Path.Combine(folder, "request.json"), Data.Bytes(new UpdateRequest(plan, process.Id, process.StartTime.ToUniversalTime().Ticks)));
        return folder;
    }
    internal static Process StartWatcher(string folder)
    {
        var start = new ProcessStartInfo(Path.Combine(folder, "runner", "QDS Mod Manager.exe")) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--self-update-watch"); start.ArgumentList.Add(folder);
        return Process.Start(start) ?? throw new IOException("Could not start the update helper.");
    }
    internal static async Task WaitForHandoff(string folder, Process watcher)
    {
        for (int i = 0; i < 900; i++)
        {
            if (File.Exists(Path.Combine(folder, "ready.json"))) return;
            if (File.Exists(Path.Combine(folder, "result.json"))) throw new IOException(Data.Read<SelfUpdateResult>(File.ReadAllBytes(Path.Combine(folder, "result.json"))).Message);
            if (watcher.HasExited) throw new IOException("The update helper stopped before installation. Mod Manager is still open.");
            await Task.Delay(100);
        }
        // The helper still waits for this process to exit and times out without writing.
        throw new IOException("Update handoff timed out. Mod Manager is still open; try again after the helper exits.");
    }
    private static UpdateRequest ReadRequest(string folder)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)); string id = Path.GetFileName(folder);
        if (!Guid.TryParseExact(id, "N", out _) || !string.Equals(folder, Path.Combine(Updates, id), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), Path.Combine(folder, "runner"), StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid updater location.");
        SelfUpdater.Guard(folder); string path = Path.Combine(folder, "request.json"); SelfUpdater.Guard(path);
        if (new FileInfo(path).Length > 65536) throw new IOException("Oversized update request.");
        var request = Data.Read<UpdateRequest>(File.ReadAllBytes(path));
        if (request.Plan.Id != id || request.ParentId < 1) throw new IOException("Invalid update request.");
        request.Plan.Release.Validate(); return request;
    }
    internal static async Task<int> Run(string mode, string folder)
    {
        UpdateRequest request;
        try { request = ReadRequest(folder); } catch { return 2; }
        if (mode == "--self-update-watch") return await Watch(folder, request);
        if (mode is not ("--self-update-apply" or "--self-update-recover")) return 2;
        string resultPath = Path.Combine(folder, mode == "--self-update-recover" ? "recovery.json" : "result.json");
        SelfUpdater.Guard(resultPath); if (File.Exists(resultPath)) return 2;
        SelfUpdateResult result;
        try
        {
            if (mode == "--self-update-recover") result = new SelfUpdater().Recover(request.Plan);
            else
            {
                // Requests in user data are not trusted as release authority.
                using var client = new ManagerReleaseClient(); await client.VerifyPublished(request.Plan.Release, default);
                string package = Path.Combine(folder, "package.zip"); SelfUpdater.Guard(package);
                if (new FileInfo(package).Length != request.Plan.Release.Size) throw new IOException("The staged update changed.");
                byte[] bytes = File.ReadAllBytes(package); _ = ManagerArchive.Open(bytes, request.Plan.Release);
                if (!SelfUpdater.OriginalIntact(request.Plan)) throw new IOException("Manager files changed since the update was prepared.");
                Process? parent = null;
                try { parent = Process.GetProcessById(request.ParentId); }
                catch (ArgumentException) { }
                using (parent)
                {
                    if (parent is not null && (parent.StartTime.ToUniversalTime().Ticks != request.ParentStart || !string.Equals(parent.MainModule?.FileName, Path.Combine(request.Plan.Target, "QDS Mod Manager.exe"), StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("The original manager process changed.");
                    WriteNew(Path.Combine(folder, "ready.json"), Data.Bytes(new { ready = true }));
                    if (parent is not null) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)); await parent.WaitForExitAsync(timeout.Token); }
                }
                result = new SelfUpdater().Apply(request.Plan, bytes);
            }
        }
        catch (Exception error) { result = new(false, SafeOriginal(request.Plan), error.GetBaseException().Message, null); }
        WriteNew(resultPath, Data.Bytes(result)); return result.Success ? 0 : 1;
    }
    private static bool SafeOriginal(SelfUpdatePlan plan) { try { return SelfUpdater.OriginalIntact(plan); } catch { return false; } }
    private static async Task<int> Watch(string folder, UpdateRequest request)
    {
        SelfUpdateResult result;
        try
        {
            await Worker("--self-update-apply", folder);
            string resultPath = Path.Combine(folder, "result.json");
            if (!File.Exists(resultPath)) { await Worker("--self-update-recover", folder); resultPath = Path.Combine(folder, "recovery.json"); }
            result = Data.Read<SelfUpdateResult>(File.ReadAllBytes(resultPath));
            if (!result.SafeToStart && File.Exists(Path.Combine(folder, "ready.json")))
            { await Worker("--self-update-recover", folder); result = Data.Read<SelfUpdateResult>(File.ReadAllBytes(Path.Combine(folder, "recovery.json"))); }
        }
        catch (Exception error)
        {
            result = new(false, SafeOriginal(request.Plan), error is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 } ? "Update canceled. Administrator permission was not granted." : error.GetBaseException().Message, null);
            string resultPath = Path.Combine(folder, "result.json"); if (!File.Exists(resultPath)) WriteNew(resultPath, Data.Bytes(result));
        }
        if (!File.Exists(Path.Combine(folder, "ready.json"))) return 1;
        // Restart from this unelevated supervisor, never from the elevated installer.
        if (result.SafeToStart && !SelfUpdater.IsRunning(request.Plan.Target))
        {
            string notice = Path.Combine(Updates, "last-result.json"); SelfUpdater.Guard(notice);
            File.WriteAllBytes(notice, Data.Bytes(new UpdateNotice(request.Plan.Target, result)));
            Process.Start(new ProcessStartInfo(Path.Combine(request.Plan.Target, "QDS Mod Manager.exe")) { UseShellExecute = false, WorkingDirectory = request.Plan.Target });
        }
        else if (!result.SafeToStart) MessageBox.Show(result.Message, "Mod Manager update", MessageBoxButton.OK, MessageBoxImage.Error);
        return result.Success ? 0 : 1;
    }
    private static async Task Worker(string mode, string folder)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add(mode); start.ArgumentList.Add(folder);
        using var child = Process.Start(start) ?? throw new IOException("Could not start the update installer."); await child.WaitForExitAsync();
    }
    private static void WriteNew(string path, byte[] bytes)
    {
        SelfUpdater.Guard(path); string stage = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            SelfUpdater.Guard(path); File.Move(stage, path);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
    internal static string? ConsumeNotice()
    {
        try
        {
            string path = Path.Combine(Updates, "last-result.json"); if (!File.Exists(path)) return null; SelfUpdater.Guard(path);
            var notice = Data.Read<UpdateNotice>(File.ReadAllBytes(path));
            if (!string.Equals(notice.Target, Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase)) return null;
            File.Move(path, Path.Combine(Updates, "notice-" + Guid.NewGuid().ToString("N") + ".json")); return notice.Result.Message;
        }
        catch { return null; }
    }
}
