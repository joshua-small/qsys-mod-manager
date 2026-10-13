namespace QdsModManager;

public sealed record ProfileMod(string Id, string Version, string Sha256, bool Enabled);
public sealed record ModProfile(int Schema, string Id, string Name, string DesignerVersion, string HostSha256, string CommonSha256, ProfileMod[] Mods)
{
    public override string ToString() => Name;
    public static ModProfile Create(string name, IEnumerable<InstalledMod>? mods = null, string? id = null) => new(1, id ?? Guid.NewGuid().ToString("N"), name, "10.5", Catalog.HostHash, Catalog.CommonHash,
        (mods ?? []).OrderBy(m => m.Id).Select(m => new ProfileMod(m.Id, m.Version, m.ArchiveHash, m.Enabled)).ToArray());
    public void Validate()
    {
        if (Schema != 1 || !Guid.TryParseExact(Id, "N", out _) || string.IsNullOrWhiteSpace(Name) || Name.Length > 100 || Name.Any(char.IsControl)
            || Mods is null || Mods.Length > Catalog.Mods.Length || Mods.Select(m => m.Id).Distinct().Count() != Mods.Length || DesignerVersion != "10.5") throw new InvalidDataException("Invalid or unsupported profile.");
        Data.CheckHash(HostSha256); Data.CheckHash(CommonSha256);
        foreach (var mod in Mods) { Catalog.Mod(mod.Id); Data.CheckHash(mod.Sha256); if (!Version.TryParse(mod.Version, out var v) || v.Build < 0) throw new InvalidDataException("Invalid pinned version."); }
    }
    public void RequireCompatible() { Validate(); if (HostSha256 != Catalog.HostHash || CommonSha256 != Catalog.CommonHash) throw new InvalidDataException("Profile requires a different Designer build."); }
    public static ModProfile Import(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Profile is too large.");
        var profile = Data.Read<ModProfile>(File.ReadAllBytes(path)); profile.Validate();
        return profile with { Id = Guid.NewGuid().ToString("N") };
    }
}
public sealed record ProfilePackage(string Id, string Path, string Sha256);
public sealed record ProfileChange(ModProfile Profile, ProfilePackage[] Packages, bool Suspended);
public sealed record ProfileAssignment(string Target, string ProfileId, string Stamp, string? AppliedStamp = null);
public sealed record ProfileStore(int Schema, ModProfile[] Profiles, ProfileAssignment[] Assignments);

/// <summary>Reusable definitions; each installation's journaled snapshot remains the applied truth.</summary>
public sealed class ProfileLibrary
{
    private readonly string path;
    public ProfileStore Store { get; private set; }
    public ProfileLibrary(string directory)
    {
        path = Path.Combine(directory, "profiles.json");
        Store = File.Exists(path) ? Data.Read<ProfileStore>(File.ReadAllBytes(path)) : new(2, [], []);
        if (Store.Schema is not (1 or 2) || Store.Profiles.Length > 1000 || Store.Profiles.Select(p => p.Id).Distinct().Count() != Store.Profiles.Length) throw new InvalidDataException("Invalid profile library.");
        foreach (var profile in Store.Profiles) profile.Validate();
    }
    private void Persist()
    {
        Store = Store with { Schema = 2 };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); string temp = path + "." + Guid.NewGuid().ToString("N");
        try { using (var stream = new FileStream(temp, FileMode.CreateNew)) { stream.Write(Data.Bytes(Store)); stream.Flush(true); } File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Save(ModProfile profile)
    {
        profile.Validate(); Store = Store with { Profiles = Store.Profiles.Where(p => p.Id != profile.Id).Append(profile).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray() }; Persist();
    }
    public void Reconcile(Inspection inspection)
    {
        if (inspection.Pending is not null || inspection.Profile is null) return;
        var previous = Store.Assignments.FirstOrDefault(a => string.Equals(a.Target, inspection.Target, StringComparison.OrdinalIgnoreCase));
        // Catalog growth changes the preview's file universe, not an installation's applied pins.
        // Legacy assignments compare the original 0.6 file order during this one-time migration.
        string appliedStamp = inspection.AppliedStamp ?? inspection.Stamp;
        bool appliedChanged = previous is not null && (previous.AppliedStamp is { } savedStamp
            ? savedStamp != appliedStamp : previous.Stamp != (inspection.LegacyStamp ?? inspection.Stamp));
        if (!Store.Profiles.Any(p => p.Id == inspection.Profile.Id) || appliedChanged)
        {
            var saved = Store.Profiles.FirstOrDefault(p => p.Id == inspection.Profile.Id);
            Save(inspection.Profile with { Name = saved?.Name ?? inspection.Profile.Name });
        }
        if (previous?.Stamp == inspection.Stamp && previous.ProfileId == inspection.Profile.Id && previous.AppliedStamp == appliedStamp) return;
        Store = Store with { Assignments = Store.Assignments.Where(a => !string.Equals(a.Target, inspection.Target, StringComparison.OrdinalIgnoreCase)).Append(new(inspection.Target, inspection.Profile.Id, inspection.Stamp, appliedStamp)).ToArray() }; Persist();
    }
    public void Delete(string id)
    {
        if (Store.Assignments.Any(a => a.ProfileId == id)) throw new InvalidOperationException("This profile is assigned to an installation. Switch that installation to another profile first.");
        Store = Store with { Profiles = Store.Profiles.Where(p => p.Id != id).ToArray() }; Persist();
    }
}
