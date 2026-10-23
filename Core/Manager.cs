using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;

namespace QdsModManager;

public sealed record InstalledMod(string Id, string Version, string ArchiveHash, bool Enabled);
public sealed record UntestedRuntimeOptIn(int Schema, string Target, string HostSha256, string CommonSha256);
public sealed record RegistryState(int Schema, Dictionary<string, InstalledMod> Mods, ModProfile? Profile = null, bool Suspended = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] UntestedRuntimeOptIn? UntestedRuntime = null);
public sealed record DesignerBuild(string HostSha256, string CommonSha256, bool Tested)
{
    public const string UntestedWarning = "WARNING: Untested on this version. Use at your own risk.";
}
public sealed record OperationPlan(int Schema, string Action, string Target, string ModId, string? PackagePath, string? PackageHash, string ExpectedStamp, string[] ChangedFiles, ProfileChange? Group = null, SignedCatalog? CatalogProof = null, DesignerBuild? ExpectedBuild = null, bool UntestedVersionAcknowledged = false);
public sealed record Journal(int Schema, string Id, string Action, string Status, Dictionary<string, string?> Before, Dictionary<string, string?> After, string? BeforeState, string? AfterState, string AfterStamp, string? Error, string? FileSetSha256 = null);
public sealed record ModStatus(string Id, string Name, string Version, string Status, bool Managed, bool Enabled, string Description);
public sealed record Inspection(string Target, bool Running, string Stamp, ModStatus[] Mods, string? Pending, bool CanUndo, ModProfile? Profile = null, bool Suspended = false, string[]? Unmanaged = null, string? AppliedStamp = null, string? LegacyStamp = null, DesignerBuild? Build = null, UntestedRuntimeOptIn? UntestedRuntime = null);
public sealed record ApplyResult(bool Success, string Message, string? Transaction);

/// <summary>Only catalog-approved file layouts may be changed; vendor and unmanaged files are preserved.</summary>
public sealed class Manager
{
    private readonly string root, registry;
    private readonly string hostHash, commonHash;
    private readonly Func<string, bool> running;
    // Used only by the separate test assembly to inject failures after file writes.
    internal Action<int>? AfterWrite;
    public Manager(string target) : this(target, Catalog.HostHash, Catalog.CommonHash, IsRunning) { }
    internal Manager(string target, string host, string common, Func<string, bool> running)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)); registry = Path.Combine(root, ".qds-mod-manager");
        hostHash = host; commonHash = common; this.running = running;
    }
    private static bool IsRunning(string root)
    {
        foreach (var process in Process.GetProcessesByName("Q-Sys Designer"))
            using (process)
                try { if (string.Equals(Path.GetDirectoryName(process.MainModule?.FileName), root, StringComparison.OrdinalIgnoreCase)) return true; }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return true; }
        return false;
    }
    public static string[] Discover()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "QSC");
        return Directory.Exists(directory) ? Directory.GetDirectories(directory, "Q-SYS Designer*").Where(p => File.Exists(Path.Combine(p, "Q-Sys Designer.exe"))).OrderByDescending(p => p.Contains("10.5")).ThenByDescending(p => p).ToArray() : Array.Empty<string>();
    }
    private DesignerBuild Host()
    {
        Guard(root); Guard(registry);
        string ReadHash(string name)
        {
            string path = Path.Combine(root, name); Guard(path);
            if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new InvalidDataException("The selected Designer installation is incomplete: " + name + " is missing or empty. No changes were made.");
            return Data.HashFile(path);
        }
        string host = ReadHash("Q-Sys Designer.dll"), common = ReadHash("ModuleCommon.dll");
        return new(host, common, host == hostHash && common == commonHash);
    }
    private void RequireBuild(DesignerBuild expected)
    {
        if (Host() != expected) throw new InvalidOperationException("The Designer installation changed while preparing this operation. Refresh and try again.");
    }
    // Refuse linked/reparse-point paths, including existing ancestors. Never
    // follow a package or registry path out of the selected installation.
    private static void Guard(string path)
    {
        for (string? cursor = Path.GetFullPath(path); cursor is not null; cursor = Path.GetDirectoryName(cursor))
            try { if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked folders/files are not supported: " + cursor); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
    }
    private string FilePath(string name) { Catalog.FileName(name); string path = Path.Combine(root, name); Guard(path); return path; }
    private string Local(string relative) { string path = Path.Combine(registry, relative); Guard(path); return path; }
    private static string? HashIfFile(string path)
    {
        if (Directory.Exists(path)) throw new IOException("A directory occupies an expected file: " + path);
        return File.Exists(path) ? Data.HashFile(path) : null;
    }
    private Dictionary<string, string?> Current() => Catalog.FileNames.ToDictionary(n => n, n => HashIfFile(FilePath(n)), StringComparer.Ordinal);
    private static string Stamp(Dictionary<string, string?> files, string? state) => Data.Hash(Data.Bytes(new { files, state }));
    private string Stamp() => Stamp(Current(), HashIfFile(Local("state.json")));
    // Historical journals cover the approved file set at their creation, in its original order.
    private string JournalStamp(Journal journal) => Stamp(journal.After.Keys.ToDictionary(n => n, n => HashIfFile(FilePath(n))), HashIfFile(Local("state.json")));
    private RegistryState State()
    {
        string path = Local("state.json");
        var state = File.Exists(path) ? Data.Read<RegistryState>(File.ReadAllBytes(path)) : new(1, new());
        if (state.Schema is not (1 or 2) || state.Mods.Count > Catalog.Mods.Length) throw new InvalidDataException("Unsupported manager state.");
        if (state.UntestedRuntime is { } consent)
        {
            if (consent.Schema != 1 || !Path.IsPathFullyQualified(consent.Target)) throw new InvalidDataException("Invalid untested runtime opt-in.");
            Data.CheckHash(consent.HostSha256); Data.CheckHash(consent.CommonSha256);
        }
        if (state.Profile is not null)
        {
            state.Profile.RequireCompatible();
            if (!state.Profile.Mods.OrderBy(m => m.Id).SequenceEqual(state.Mods.Values.OrderBy(m => m.Id).Select(m => new ProfileMod(m.Id, m.Version, m.ArchiveHash, m.Enabled)))) throw new InvalidDataException("Profile disagrees with installed state.");
        }
        foreach (var (id, entry) in state.Mods)
        {
            Catalog.Mod(id); Data.CheckHash(entry.ArchiveHash);
            if (id != entry.Id || !Version.TryParse(entry.Version, out _)) throw new InvalidDataException("Invalid installed package record.");
            var package = Stored(entry);
            if (package.Manifest.Id != id || package.Manifest.Version != entry.Version) throw new InvalidDataException("Stored package disagrees with installed state.");
        }
        return state;
    }
    private ModPackage Stored(InstalledMod mod)
    {
        Data.CheckHash(mod.ArchiveHash);
        var package = ModPackage.Open(Local("packages/" + mod.ArchiveHash + ".qdsmod"));
        if (package.ArchiveHash != mod.ArchiveHash) throw new InvalidDataException("Stored package was changed.");
        return package;
    }
    private void VerifyManaged(RegistryState state, Dictionary<string, string?> current)
    {
        foreach (var entry in state.Mods.Values)
            foreach (var file in Stored(entry).Manifest.Files.Where(f => !f.Shared))
                if (current[file.Name] != (entry.Enabled && !state.Suspended ? file.Sha256 : null))
                    throw new InvalidDataException("An installed mod file changed outside the manager: " + file.Name + ". Restore that file before continuing.");
        foreach (var file in Catalog.Mods.SelectMany(m => m.Layouts).SelectMany(l => l.Files).Where(f => f.Shared).Distinct())
            if (current[file.Name] is { } value && value != file.Sha256) throw new InvalidDataException("A shared dependency has a different version: " + file.Name + ". It will not be overwritten.");
    }
    private Journal[] Journals()
    {
        string directory = Local("history"); if (!Directory.Exists(directory)) return Array.Empty<Journal>();
        return Directory.GetFiles(directory, "*.json").OrderDescending().Select(p =>
        {
            Guard(p); var journal = Data.Read<Journal>(File.ReadAllBytes(p));
            if (journal.Schema is not (1 or 2) || journal.Id != Path.GetFileNameWithoutExtension(p) || journal.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new InvalidDataException("Invalid transaction journal.");
            foreach (var map in new[] { journal.Before, journal.After }) foreach (var (name, hash) in map) { Catalog.FileName(name); if (hash is not null) Data.CheckHash(hash); }
            foreach (var hash in new[] { journal.BeforeState, journal.AfterState }) if (hash is not null) Data.CheckHash(hash);
            if (!journal.Before.Keys.Order().SequenceEqual(journal.After.Keys.Order()) || journal.Before.Count == 0) throw new InvalidDataException("Incomplete transaction journal.");
            var names = journal.Before.Keys.Order(StringComparer.Ordinal).ToArray();
            if (journal.Schema == 1 ? !names.SequenceEqual(Catalog.LegacyJournalFiles) : journal.FileSetSha256 != Data.Hash(Data.Bytes(names)))
                throw new InvalidDataException("Transaction journal file set was changed or truncated.");
            return journal;
        }).ToArray();
    }
    public Inspection Inspect(IEnumerable<ModPackage> available)
    {
        var build = Host(); var current = Current(); var state = State(); var history = Journals();
        var rows = Catalog.Mods.Select(mod =>
        {
            if (state.Mods.TryGetValue(mod.Id, out var entry))
            {
                var package = Stored(entry); bool enabled = entry.Enabled && !state.Suspended; bool intact = package.Manifest.Files.Where(f => !f.Shared).All(f => current[f.Name] == (enabled ? f.Sha256 : null));
                return new ModStatus(mod.Id, mod.Name, entry.Version, !intact ? "Files changed outside manager" : state.Suspended && entry.Enabled ? "Suspended" : enabled ? "Enabled" : "Disabled", true, enabled, mod.Description);
            }
            var match = available.FirstOrDefault(p => p.Manifest.Id == mod.Id && p.Manifest.Files.All(f => current[f.Name] == f.Sha256));
            bool present = mod.Layouts.SelectMany(l => l.Files).Where(f => !f.Shared).Any(f => current[f.Name] is not null);
            return new ModStatus(mod.Id, mod.Name, match?.Manifest.Version ?? (present ? "Unknown" : "—"), match is not null ? "Installed · not managed yet" : present ? "Unrecognized installed files" : "Not installed", false, present, mod.Description);
        }).ToArray();
        var latest = history.FirstOrDefault(j => j.Status == "Complete");
        var unmanaged = Directory.GetFiles(root, "Designer.Modules.*.dll").Select(Path.GetFileName).Where(n => n is not null && !state.Mods.Values.Any(m => Catalog.Mod(m.Id).Module == n)).Cast<string>().Order().ToArray();
        var profile = AppliedProfile(state);
        string appliedStamp = Data.Hash(Data.Bytes(new { profile, state.Suspended }));
        string legacyStamp = Stamp(Catalog.LegacyFileOrder.ToDictionary(n => n, n => current[n]), HashIfFile(Local("state.json")));
        return new(root, running(root), Stamp(), rows, history.FirstOrDefault(j => j.Status == "Pending")?.Id, latest is not null && latest.AfterStamp == JournalStamp(latest), profile, state.Suspended, unmanaged, appliedStamp, legacyStamp, build, state.UntestedRuntime);
    }
    private ModProfile AppliedProfile(RegistryState state) => state.Profile ?? ModProfile.Create(Path.GetFileName(root) + " · initial", state.Mods.Values, Data.Hash(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))[..32]);
    public ModPackage[] CachedPackages()
    {
        Host(); _ = State(); string directory = Local("packages"); if (!Directory.Exists(directory)) return [];
        return Directory.GetFiles(directory, "*.qdsmod").Select(path => { Guard(path); var package = ModPackage.Open(path); if (package.ArchiveHash != Path.GetFileNameWithoutExtension(path)) throw new InvalidDataException("Stored package was changed."); return package; }).ToArray();
    }
    public string[] OperationErrors() => Journals().Where(j => j.Error is not null).Take(20).Select(j => j.Action + ": " + j.Error).ToArray();
    private sealed record Change(RegistryState? State, Dictionary<string, byte[]?> Files, ModPackage? Package, ModPackage[]? Packages = null);
    private Change Build(string action, string id, string? packagePath)
    {
        Host(); var current = Current(); var state = State(); VerifyManaged(state, current);
        if (Journals().Any(j => j.Status == "Pending")) throw new InvalidOperationException("An interrupted change needs recovery first.");
        var files = new Dictionary<string, byte[]?>(StringComparer.Ordinal); ModPackage? package = null;
        if (action == "Undo")
        {
            var last = Journals().FirstOrDefault(j => j.Status == "Complete") ?? throw new InvalidOperationException("No previous change is available.");
            if (last.AfterStamp != JournalStamp(last)) throw new InvalidOperationException("The installation changed after the last operation; undo will not overwrite it.");
            foreach (var (name, hash) in last.Before) if (current[name] != hash) files[name] = Object(hash);
            return new(last.BeforeState is null ? null : Data.Read<RegistryState>(Object(last.BeforeState)!), files, null);
        }
        if (state.Suspended) throw new InvalidOperationException("Turn managed mods on before changing individual mods.");
        var profile = AppliedProfile(state);
        var mod = Catalog.Mod(id); state.Mods.TryGetValue(id, out var installed);
        switch (action)
        {
            case "Adopt": case "Install":
                package = ModPackage.Open(packagePath ?? throw new InvalidOperationException("Choose a package first."));
                if (package.Manifest.Id != id) throw new InvalidDataException("Package selection changed.");
                if (installed is not null && RequiresSchematicToolsMigration(id, installed.Version, package.Manifest.Version))
                    throw new InvalidOperationException("Use Split Schematic Tools before installing the diagnostics-only successor. The legacy bundle's features must be migrated first.");
                if (action == "Adopt" && (installed is not null || package.Manifest.Files.Any(f => current[f.Name] != f.Sha256))) throw new InvalidOperationException("Only an exact existing package can be registered.");
                var previousFiles = installed is null ? [] : Stored(installed).Manifest.Files;
                foreach (var file in package.Manifest.Files)
                {
                    bool owned = previousFiles.Any(f => !f.Shared && f.Name == file.Name);
                    if (!file.Shared && !owned && current[file.Name] is not null && current[file.Name] != file.Sha256) throw new InvalidOperationException("An existing file is not recognized. It will not be overwritten: " + file.Name);
                    if (current[file.Name] != file.Sha256) files[file.Name] = package.Files[file.Name];
                }
                if (installed is not null)
                    foreach (var previous in previousFiles.Where(f => !f.Shared && !package.Files.ContainsKey(f.Name)))
                        if (current[previous.Name] is not null) files[previous.Name] = null;
                state.Mods[id] = new(id, package.Manifest.Version, package.ArchiveHash, true);
                break;
            case "Enable":
                if (installed is null || installed.Enabled) throw new InvalidOperationException("Choose a disabled managed mod.");
                package = Stored(installed);
                foreach (var file in package.Manifest.Files) if (current[file.Name] != file.Sha256) files[file.Name] = package.Files[file.Name];
                state.Mods[id] = installed with { Enabled = true }; break;
            case "Disable": case "Remove":
                if (installed is null) throw new InvalidOperationException("Register the existing mod before changing it.");
                if (action == "Disable" && !installed.Enabled) throw new InvalidOperationException("This mod is already disabled.");
                if (installed.Enabled) foreach (var file in Stored(installed).Manifest.Files.Where(f => !f.Shared)) files[file.Name] = null;
                if (action == "Remove") state.Mods.Remove(id); else state.Mods[id] = installed with { Enabled = false };
                break;
            default: throw new InvalidDataException("Unsupported manager action.");
        }
        state = state with { Schema = 2, Profile = profile with { Mods = ModProfile.Create(profile.Name, state.Mods.Values).Mods } };
        ValidateDependencies(state, package is null ? [] : [package]);
        return new(state, files, package);
    }
    private Change BuildGroup(ProfileChange group)
    {
        Host(); group.Profile.RequireCompatible(); var current = Current(); var state = State(); VerifyManaged(state, current);
        if (Journals().Any(j => j.Status == "Pending")) throw new InvalidOperationException("Recover the interrupted change first.");
        if (group.Packages.Length != group.Profile.Mods.Length || group.Packages.Select(p => p.Id).Distinct().Count() != group.Packages.Length) throw new InvalidDataException("Incomplete profile packages.");
        var files = new Dictionary<string, byte[]?>(StringComparer.Ordinal); var resolved = new List<ModPackage>(); var desired = new Dictionary<string, InstalledMod>();
        foreach (var pin in group.Profile.Mods.OrderBy(m => m.Id))
        {
            if (state.Mods.TryGetValue(pin.Id, out var prior) && RequiresSchematicToolsMigration(pin.Id, prior.Version, pin.Version))
                throw new InvalidOperationException("Use Split Schematic Tools before installing the diagnostics-only successor. The legacy bundle's features must be migrated first.");
            var reference = group.Packages.SingleOrDefault(p => p.Id == pin.Id) ?? throw new InvalidDataException("Missing package: " + pin.Id);
            var package = ModPackage.Open(reference.Path);
            if (reference.Sha256 != pin.Sha256 || package.ArchiveHash != pin.Sha256 || package.Manifest.Id != pin.Id || package.Manifest.Version != pin.Version) throw new InvalidDataException("Pinned package changed: " + pin.Id);
            resolved.Add(package); desired.Add(pin.Id, new(pin.Id, pin.Version, pin.Sha256, pin.Enabled));
            var previousFiles = state.Mods.TryGetValue(pin.Id, out var installed) ? Stored(installed).Manifest.Files : [];
            foreach (var file in package.Manifest.Files)
            {
                bool enabled = pin.Enabled && !group.Suspended;
                if (file.Shared && !enabled) continue;
                string? hash = enabled || file.Shared ? file.Sha256 : null;
                bool owned = previousFiles.Any(f => !f.Shared && f.Name == file.Name);
                if (!file.Shared && !owned && current[file.Name] is not null && current[file.Name] != hash) throw new InvalidOperationException("Unmanaged file remains and prevents this profile: " + file.Name);
                if (current[file.Name] != hash) files[file.Name] = hash is null ? null : package.Files[file.Name];
            }
        }
        foreach (var previous in state.Mods.Values)
            foreach (var file in Stored(previous).Manifest.Files.Where(f => !f.Shared))
                if (!resolved.Any(p => p.Manifest.Id == previous.Id && p.Files.ContainsKey(file.Name)) && current[file.Name] is not null) files[file.Name] = null;
        var next = new RegistryState(2, desired, group.Profile, group.Suspended);
        ValidateDependencies(next, resolved);
        return new(next, files, null, resolved.ToArray());
    }
    private void ValidateDependencies(RegistryState desired, IEnumerable<ModPackage> incoming)
    {
        var available = incoming.ToDictionary(p => p.ArchiveHash);
        var manifests = desired.Mods.Values.Where(m => m.Enabled).Select(m => available.TryGetValue(m.ArchiveHash, out var package) ? package.Manifest : Stored(m).Manifest).ToArray();
        Requirements.Validate(manifests);
        var before = State();
        foreach (var conflict in manifests.SelectMany(m => m.Conflicts ?? []))
            if (!before.Mods.ContainsKey(conflict.Id) && File.Exists(FilePath(Catalog.Mod(conflict.Id).Module)))
                throw new InvalidOperationException("An unmanaged conflicting mod is present: " + conflict.Id + ". Register it before migrating.");
    }
    public OperationPlan PrepareInstall(ModPackage selected, IEnumerable<ModPackage> available)
        => PrepareInstall([selected], available);
    public OperationPlan PrepareInstall(IEnumerable<ModPackage> selected, IEnumerable<ModPackage> available)
    {
        Host(); var state = State();
        if (state.Suspended) throw new InvalidOperationException("Turn managed mods on before installing a mod.");
        var existing = state.Mods.Values.Select(m => (Package: Stored(m), m.Enabled)).ToArray();
        var resolved = Requirements.Resolve(existing, selected, available);
        var mods = state.Mods.Values.ToDictionary(m => m.Id);
        foreach (var package in resolved) mods[package.Manifest.Id] = new(package.Manifest.Id, package.Manifest.Version, package.ArchiveHash, true);
        var profile = AppliedProfile(state) with { Mods = ModProfile.Create("", mods.Values).Mods };
        return PrepareProfile(profile, existing.Select(m => m.Package).Concat(resolved), action: "InstallSet");
    }
    public static readonly string[] SchematicToolsReplacementIds = ["mods-core", "better-packing", "named-signal-colors", "snapshot-inspector", "better-shortcuts", "color-palette-aligned", "icons-filter", "component-controls-info-filter"];
    public static bool RequiresSchematicToolsMigration(string id, string installed, string candidate) => id == "schematic-tools"
        && Version.Parse(installed) < new Version(0,17,0) && Version.Parse(candidate) >= new Version(0,17,0);
    public OperationPlan PrepareSchematicToolsMigration(IEnumerable<ModPackage> available)
    {
        Host(); var state = State();
        if (!state.Mods.TryGetValue("schematic-tools", out var old) || Version.Parse(old.Version) >= new Version(0,17,0))
            throw new InvalidOperationException("Choose an installation with a managed legacy Schematic Tools bundle.");
        var packages = available.Concat(state.Mods.Values.Select(Stored)).DistinctBy(p => p.ArchiveHash).ToArray();
        var mods = state.Mods.Values.Where(m => m.Id != "schematic-tools").ToDictionary(m => m.Id);
        foreach (var id in SchematicToolsReplacementIds)
        {
            if (mods.ContainsKey(id)) continue; // Preserve existing exact pins in a partially migrated profile.
            var package = packages.Where(p => p.Manifest.Id == id).OrderByDescending(p => Version.Parse(p.Manifest.Version)).FirstOrDefault()
                ?? throw new InvalidOperationException("Migration requires package: " + id);
            mods[id] = new(id, package.Manifest.Version, package.ArchiveHash, old.Enabled);
        }
        var profile = AppliedProfile(state) with { Mods = ModProfile.Create("", mods.Values).Mods };
        return PrepareProfile(profile, packages, state.Suspended, "Migration");
    }
    public OperationPlan PrepareProfile(ModProfile profile, IEnumerable<ModPackage> packages, bool suspended = false, string action = "Profile")
    {
        if (action is not ("Profile" or "Updates" or "Suspend" or "Resume" or "InstallSet" or "Migration")) throw new InvalidDataException("Unsupported grouped action.");
        var available = packages.ToArray();
        var group = new ProfileChange(profile, profile.Mods.Select(pin => { var p = available.FirstOrDefault(p => p.ArchiveHash == pin.Sha256) ?? throw new InvalidOperationException("Missing exact package: " + pin.Id + " " + pin.Version); return new ProfilePackage(pin.Id, p.Path, p.ArchiveHash); }).ToArray(), suspended);
        var change = BuildGroup(group);
        return new(2, action, root, "", null, null, Stamp(), change.Files.Keys.ToArray(), group, Catalog.Proof, Host());
    }
    public OperationPlan Prepare(string action, string id = "", string? packagePath = null)
    {
        if (action == "Recover")
        {
            Host(); var pending = Journals().SingleOrDefault(j => j.Status == "Pending") ?? throw new InvalidOperationException("Nothing needs recovery.");
            return new(1, action, root, "", null, null, Stamp(), pending.Before.Where(k => k.Value != pending.After[k.Key]).Select(k => k.Key).ToArray(), CatalogProof: Catalog.Proof, ExpectedBuild: Host());
        }
        var change = Build(action, id, packagePath);
        return new(1, action, root, id, change.Package?.Path, change.Package?.ArchiveHash, Stamp(), change.Files.Keys.ToArray(), CatalogProof: Catalog.Proof, ExpectedBuild: Host());
    }
    public ApplyResult Apply(OperationPlan plan)
    {
        if (plan.Schema is not (1 or 2) || !string.Equals(root, plan.Target, StringComparison.OrdinalIgnoreCase) || (plan.Group is not null && (plan.Schema != 2 || plan.Action is not ("Profile" or "Updates" or "Suspend" or "Resume" or "InstallSet" or "Migration")))) throw new InvalidDataException("Invalid operation target.");
        var build = Host();
        if (plan.ExpectedBuild is not null && plan.ExpectedBuild != build) throw new InvalidOperationException("The Designer installation changed while preparing this operation. Refresh and try again.");
        if (!build.Tested && (plan.ExpectedBuild is null || !plan.UntestedVersionAcknowledged)) throw new InvalidOperationException(DesignerBuild.UntestedWarning + " Review and confirm the change before applying it.");
        Directory.CreateDirectory(registry);
        using var ownership = new FileStream(Local("operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (plan.ExpectedStamp != Stamp()) throw new InvalidOperationException("The installation changed while preparing this operation. Refresh and try again.");
        if (plan.Action == "Recover") return Recover(build);
        var change = plan.Group is null ? Build(plan.Action, plan.ModId, plan.PackagePath) : BuildGroup(plan.Group);
        if (change.Package?.ArchiveHash != plan.PackageHash || !change.Files.Keys.SequenceEqual(plan.ChangedFiles)) throw new InvalidOperationException("The package or operation changed while preparing this change.");
        if (change.Files.Count > 0 && running(root)) throw new InvalidOperationException("Close this QDS installation before changing its mods. Your open designs have not been touched.");
        // Consent travels with the journaled installation state, never with a
        // reusable profile. Undo/recovery restore their exact prior consent.
        if (plan.Action != "Undo" && change.State is { } next)
        {
            bool enableRuntime = !build.Tested && plan.UntestedVersionAcknowledged && !next.Suspended
                && next.Mods.TryGetValue("mods-core", out var core) && core.Enabled;
            change = change with { State = next with { UntestedRuntime = enableRuntime
                ? new(1, root, build.HostSha256, build.CommonSha256) : null } };
        }
        if (change.Package is not null) StorePackage(change.Package);
        foreach (var package in change.Packages ?? []) StorePackage(package);
        var before = Current(); string? beforeState = PutFile(Local("state.json"));
        foreach (var name in before.Keys) if (before[name] is not null) PutFile(FilePath(name));
        var after = new Dictionary<string, string?>(before, StringComparer.Ordinal);
        foreach (var (name, bytes) in change.Files) after[name] = bytes is null ? null : Put(bytes);
        string? afterState = change.State is null ? null : Put(Data.Bytes(change.State));
        string id = DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N");
        var journal = new Journal(2, id, plan.Action, "Pending", before, after, beforeState, afterState, Stamp(after, afterState), null,
            Data.Hash(Data.Bytes(before.Keys.Order(StringComparer.Ordinal).ToArray())));
        SaveJournal(journal);
        try
        {
            int count = 0;
            foreach (var (name, bytes) in change.Files) { RequireBuild(build); WriteTarget(name, bytes); AfterWrite?.Invoke(++count); }
            RequireBuild(build);
            WriteAtomic(Local("state.json"), Object(afterState));
            AfterWrite?.Invoke(++count);
            if (Stamp() != journal.AfterStamp) throw new IOException("Post-install verification failed.");
            RequireBuild(build); SaveJournal(journal with { Status = "Complete" });
            return new(true, plan.Action == "Adopt" ? "Existing mods registered. Their files are unchanged." : "Change complete. Start QDS to use this mod configuration.", id);
        }
        catch (Exception failure)
        {
            try { Rollback(journal); SaveJournal(journal with { Status = "RolledBack", Error = failure.Message }); }
            catch (Exception recovery) { throw new IOException("Change interrupted. Backups are preserved; use Recover after resolving the problem. " + recovery.Message, failure); }
            throw new IOException("The change failed and was rolled back: " + failure.Message, failure);
        }
    }
    private ApplyResult Recover(DesignerBuild build)
    {
        var pending = Journals().Single(j => j.Status == "Pending");
        if (pending.Before.Any(k => k.Value != pending.After[k.Key]) && running(root)) throw new InvalidOperationException("Close this QDS installation before recovering its files.");
        Rollback(pending, build); SaveJournal(pending with { Status = "RolledBack", Error = "Recovered an interrupted operation." });
        return new(true, "Interrupted change recovered from verified backups.", pending.Id);
    }
    private void Rollback(Journal journal, DesignerBuild? expectedBuild = null)
    {
        var current = Current();
        foreach (var name in journal.Before.Keys) if (current[name] != journal.Before[name] && current[name] != journal.After[name]) throw new IOException("Recovery found an externally changed file: " + name);
        var state = HashIfFile(Local("state.json"));
        if (state != journal.BeforeState && state != journal.AfterState) throw new IOException("Manager state changed externally during recovery.");
        // Verify every backup before touching any destination.
        foreach (var hash in journal.Before.Values.Append(journal.BeforeState)) _ = Object(hash);
        // Automatic failure rollback may restore our files even if a host update
        // caused the failure. Explicit recovery stays bound to its preview.
        void CheckBuild() { if (expectedBuild is not null) RequireBuild(expectedBuild); else Host(); }
        foreach (var (name, hash) in journal.Before) if (current[name] != hash) { CheckBuild(); WriteTarget(name, Object(hash)); }
        CheckBuild();
        WriteAtomic(Local("state.json"), Object(journal.BeforeState));
        if (JournalStamp(journal) != Stamp(journal.Before, journal.BeforeState)) throw new IOException("Recovery verification failed.");
    }
    private void StorePackage(ModPackage package)
    {
        byte[] bytes = File.ReadAllBytes(package.Path);
        if (Data.Hash(bytes) != package.ArchiveHash) throw new IOException("Package changed during preparation.");
        string path = Local("packages/" + package.ArchiveHash + ".qdsmod");
        if (File.Exists(path)) { if (Data.HashFile(path) != package.ArchiveHash) throw new IOException("Stored package was modified."); }
        else WriteAtomic(path, bytes);
    }
    private string? PutFile(string path) => File.Exists(path) ? Put(File.ReadAllBytes(path)) : null;
    private string Put(byte[] bytes)
    {
        string hash = Data.Hash(bytes), path = Local("objects/" + hash);
        if (File.Exists(path)) { if (Data.HashFile(path) != hash) throw new IOException("A backup was changed."); }
        else WriteAtomic(path, bytes);
        return hash;
    }
    private byte[]? Object(string? hash)
    {
        if (hash is null) return null; Data.CheckHash(hash); byte[] bytes = File.ReadAllBytes(Local("objects/" + hash));
        if (Data.Hash(bytes) != hash) throw new IOException("Backup integrity check failed."); return bytes;
    }
    private void SaveJournal(Journal journal) => WriteAtomic(Local("history/" + journal.Id + ".json"), Data.Bytes(journal));
    private void WriteTarget(string name, byte[]? bytes) => WriteAtomic(FilePath(name), bytes);
    private static void WriteAtomic(string path, byte[]? bytes)
    {
        Guard(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (bytes is null) { File.Delete(path); return; }
        string staging = path + ".qds-stage-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes); output.Flush(true); }
            Guard(path); File.Move(staging, path, true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}
