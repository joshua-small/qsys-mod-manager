using System.Diagnostics;
using System.Text.RegularExpressions;

namespace QdsModManager;

public sealed record SelfUpdatePlan(string Id, string Target, string CurrentVersion, ManagerRelease Release, Dictionary<string, string?> Before);
public sealed record SelfUpdateResult(bool Success, bool SafeToStart, string Message, string? Backup);
internal sealed record AppFile(string Path, string Sha256);
internal sealed record AppFiles(int Schema, string Version, AppFile[] Files);
internal sealed record AppUpdateJournal(SelfUpdatePlan Plan, Dictionary<string, string?> After, string Status);

public sealed class SelfUpdater
{
    private static readonly string[] Owned = ManagerArchive.Names.Append("installed-files.json").ToArray();
    internal Action<int>? AfterWrite;
    private readonly Func<string, bool> running;
    public SelfUpdater() : this(IsRunning) { }
    internal SelfUpdater(Func<string, bool> running) => this.running = running;
    public static bool IsRunning(string target)
    {
        foreach (var process in Process.GetProcessesByName("QDS Mod Manager")) using (process)
            try { if (string.Equals(Path.GetDirectoryName(process.MainModule?.FileName), target, StringComparison.OrdinalIgnoreCase)) return true; }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return true; }
        return false;
    }
    public static void Guard(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked update paths are not supported."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
    }
    private static string In(string root, string name) { string path = Path.Combine(root, name); Guard(path); return path; }
    private static Dictionary<string, string?> Current(string root) => Owned.ToDictionary(name => name, name =>
    {
        string path = In(root, name); if (Directory.Exists(path)) throw new IOException("A directory occupies a manager file.");
        return File.Exists(path) ? Data.HashFile(path) : null;
    }, StringComparer.Ordinal);
    private static bool Same(Dictionary<string, string?> a, Dictionary<string, string?> b) => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var hash) && hash == p.Value);
    public SelfUpdatePlan Prepare(string target, ManagerRelease release)
    {
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)); Guard(target); release.Validate();
        string version = ManagerArchive.VerifyAssembly(File.ReadAllBytes(In(target, "QDS Mod Manager.dll")), "QDS Mod Manager", null);
        ManagerArchive.VerifyAssembly(File.ReadAllBytes(In(target, "QdsModManager.Core.dll")), "QdsModManager.Core", null);
        foreach (string name in ManagerArchive.Names.Take(5)) if (!File.Exists(In(target, name))) throw new IOException("The manager installation is incomplete.");
        if (!release.IsNewerThan(version)) throw new InvalidOperationException("This manager is already at this version or newer.");
        var before = Current(target); _ = NewManifest(target, release, null, before);
        string history = In(target, ".manager-updates");
        if (Directory.Exists(history)) foreach (var directory in Directory.GetDirectories(history))
        {
            string journal = In(directory, "journal.json");
            if (File.Exists(journal) && Data.Read<AppUpdateJournal>(File.ReadAllBytes(journal)).Status == "Pending") throw new IOException("An earlier manager update needs recovery first.");
        }
        return new(Guid.NewGuid().ToString("N"), target, version, release, before);
    }
    private static byte[] NewManifest(string root, ManagerRelease release, ManagerArchive? archive, Dictionary<string, string?> before)
    {
        var preserved = new List<AppFile>();
        if (before["installed-files.json"] is not null)
        {
            var old = Data.Read<AppFiles>(File.ReadAllBytes(In(root, "installed-files.json")));
            if (old.Schema != 1 || old.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != old.Files.Length) throw new InvalidDataException("Unsupported installed manager records.");
            foreach (var file in old.Files)
            {
                Data.CheckHash(file.Sha256);
                if (ManagerArchive.Names.Contains(file.Path))
                { if (before[file.Path] != file.Sha256) throw new IOException("Manager files changed outside the installer: " + file.Path); }
                else if (Regex.IsMatch(file.Path, @"^Packages/[a-z0-9.-]+\.qdsmod$")) preserved.Add(file);
                else throw new InvalidDataException("Unexpected installed manager file record.");
            }
        }
        return Data.Bytes(new AppFiles(1, release.Version, (archive?.Files.Select(p => new AppFile(p.Key, Data.Hash(p.Value))) ?? Array.Empty<AppFile>()).Concat(preserved).ToArray()));
    }
    private static string Location(SelfUpdatePlan plan)
    {
        if (!Guid.TryParseExact(plan.Id, "N", out _) || Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.Target)) != plan.Target || !plan.Before.Keys.Order().SequenceEqual(Owned.Order())) throw new InvalidDataException("Invalid manager update plan.");
        plan.Release.Validate(); foreach (var hash in plan.Before.Values) if (hash is not null) Data.CheckHash(hash);
        return In(plan.Target, ".manager-updates/" + plan.Id);
    }
    public static bool OriginalIntact(SelfUpdatePlan plan) { _ = Location(plan); return Same(Current(plan.Target), plan.Before); }
    public SelfUpdateResult Apply(SelfUpdatePlan plan, byte[] archiveBytes)
    {
        string history = Location(plan); var archive = ManagerArchive.Open(archiveBytes, plan.Release);
        string store = In(plan.Target, ".manager-updates"); Directory.CreateDirectory(store);
        using var exclusive = new FileStream(In(store, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (running(plan.Target)) throw new IOException("Close other Mod Manager windows before installing the update.");
        if (!Same(Current(plan.Target), plan.Before)) throw new IOException("Manager files changed since the update was prepared. No files were replaced.");
        string version = ManagerArchive.VerifyAssembly(File.ReadAllBytes(In(plan.Target, "QDS Mod Manager.dll")), "QDS Mod Manager", null);
        if (version != plan.CurrentVersion || !plan.Release.IsNewerThan(version)) throw new IOException("The manager version changed since the update was prepared.");
        if (Directory.Exists(history)) throw new IOException("This update already has a transaction record.");
        var afterBytes = archive.Files.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        afterBytes.Add("installed-files.json", NewManifest(plan.Target, plan.Release, archive, plan.Before));
        Directory.CreateDirectory(history);
        foreach (var (name, hash) in plan.Before) if (hash is not null)
        {
            byte[] bytes = File.ReadAllBytes(In(plan.Target, name)); if (Data.Hash(bytes) != hash) throw new IOException("Manager changed while backing up.");
            Atomic(In(history, "before/" + name), bytes);
        }
        var journal = new AppUpdateJournal(plan, afterBytes.ToDictionary(p => p.Key, p => (string?)Data.Hash(p.Value)), "Pending");
        Atomic(In(history, "journal.json"), Data.Bytes(journal));
        try
        {
            int count = 0;
            foreach (var (name, bytes) in afterBytes.OrderBy(p => p.Key == "QDS Mod Manager.exe" ? 1 : p.Key == "installed-files.json" ? 2 : 0))
            { Atomic(In(plan.Target, name), bytes); AfterWrite?.Invoke(++count); }
            if (!Same(Current(plan.Target), journal.After)) throw new IOException("Manager update verification failed.");
            Atomic(In(history, "journal.json"), Data.Bytes(journal with { Status = "Complete" }));
            return new(true, true, "Mod Manager updated to " + plan.Release.Version + ".", history);
        }
        catch (Exception error)
        {
            try { Rollback(journal, history); return new(false, true, "Update failed; the previous manager was restored. " + error.Message, history); }
            catch (Exception restore) { return new(false, false, "Update interrupted. Backups are preserved at " + history + ". " + restore.Message, history); }
        }
    }
    public SelfUpdateResult Recover(SelfUpdatePlan plan)
    {
        string history = Location(plan), store = In(plan.Target, ".manager-updates");
        if (!File.Exists(In(history, "journal.json")))
        {
            if (OriginalIntact(plan)) return new(false, true, "Update stopped before changing the manager.", null);
            throw new IOException("No recovery record exists for the changed manager files.");
        }
        using var exclusive = new FileStream(In(store, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (running(plan.Target)) throw new IOException("Close Mod Manager before restoring its files.");
        var journal = Data.Read<AppUpdateJournal>(File.ReadAllBytes(In(history, "journal.json")));
        if (journal.Plan.Id != plan.Id || journal.Plan.Target != plan.Target || !Same(journal.Plan.Before, plan.Before) || !journal.After.Keys.Order().SequenceEqual(Owned.Order())) throw new InvalidDataException("Recovery record does not match the update.");
        foreach (var hash in journal.After.Values) if (hash is not null) Data.CheckHash(hash);
        if (journal.Status == "Complete" && Same(Current(plan.Target), journal.After)) return new(true, true, "Manager update completed.", history);
        if (journal.Status == "RolledBack" && Same(Current(plan.Target), plan.Before)) return new(false, true, "Previous manager restored.", history);
        if (journal.Status != "Pending") throw new IOException("The update state changed outside recovery.");
        Rollback(journal, history); return new(false, true, "Interrupted update recovered. The previous manager was restored.", history);
    }
    private static void Rollback(AppUpdateJournal journal, string history)
    {
        var current = Current(journal.Plan.Target); var restored = new Dictionary<string, byte[]?>();
        foreach (var (name, hash) in journal.Plan.Before)
        {
            if (current[name] != hash && current[name] != journal.After[name]) throw new IOException("Recovery will not overwrite an externally changed file: " + name);
            byte[]? bytes = hash is null ? null : File.ReadAllBytes(In(history, "before/" + name));
            if (bytes is not null && Data.Hash(bytes) != hash) throw new IOException("A manager backup was changed."); restored[name] = bytes;
        }
        foreach (var (name, bytes) in restored) if (current[name] != journal.Plan.Before[name]) Atomic(In(journal.Plan.Target, name), bytes);
        if (!Same(Current(journal.Plan.Target), journal.Plan.Before)) throw new IOException("Manager recovery verification failed.");
        Atomic(In(history, "journal.json"), Data.Bytes(journal with { Status = "RolledBack" }));
    }
    internal static void Atomic(string path, byte[]? bytes)
    {
        Guard(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (bytes is null) { File.Delete(path); return; }
        string stage = path + ".update-" + Guid.NewGuid().ToString("N");
        try { using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); } Guard(path); File.Move(stage, path, true); }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
}
