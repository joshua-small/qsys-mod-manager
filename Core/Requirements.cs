namespace QdsModManager;

public sealed record DependencyChoice(string Id, string Name, string Version, string Key, ModRequirement[] Dependencies, ModRequirement[] Conflicts)
{
    public static DependencyChoice From(ModPackage package) => new(package.Manifest.Id, package.Manifest.Name, package.Manifest.Version,
        package.ArchiveHash, package.Manifest.Dependencies ?? [], package.Manifest.Conflicts ?? []);
    public static DependencyChoice From(CatalogItem item, CatalogRelease release)
    {
        var policies = Catalog.Mod(item.Id).Layouts.Where(l => l.Schema == 3 && Requirements.Matches(new(item.Id, l.MinimumVersion!, l.BeforeVersion), release.Version)).ToArray();
        if (policies.Select(p => Data.Hash(Data.Bytes(new { p.Dependencies, p.Conflicts }))).Distinct().Count() > 1)
            throw new InvalidDataException("Ambiguous dependency policy for " + item.Name + " " + release.Version);
        var policy = policies.FirstOrDefault();
        return new(item.Id, item.Name, release.Version, release.Sha256, policy?.Dependencies ?? [], policy?.Conflicts ?? []);
    }
    internal PackageManifest Manifest => new(3, Id, Name, Version, "", Catalog.HostHash, Catalog.CommonHash, [], Dependencies, Conflicts);
}

/// <summary>Version constraints are catalog-approved data, never package scripts.</summary>
public static class Requirements
{
    public static bool Matches(ModRequirement requirement, string version)
    {
        var actual = Normalize(version);
        return actual >= Normalize(requirement.Minimum) && (requirement.Before is null || actual < Normalize(requirement.Before));
    }
    private static Version Normalize(string value)
    {
        if (!Version.TryParse(value, out var version) || version.Build < 0) throw new InvalidDataException("Dependency versions require at least major.minor.patch.");
        return new(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
    }
    internal static void ValidatePolicy(string owner, int schema, ModRequirement[]? dependencies, ModRequirement[]? conflicts, IEnumerable<string> known)
    {
        if (schema < 3)
        {
            if (dependencies is not null || conflicts is not null) throw new InvalidDataException("Dependencies require package schema 3.");
            return;
        }
        if (dependencies is null || conflicts is null) throw new InvalidDataException("Schema 3 requires explicit dependency and conflict lists.");
        var ids = known.ToHashSet(StringComparer.Ordinal);
        foreach (var list in new[] { dependencies, conflicts })
        {
            if (list.Length > 64 || list.Select(r => r.Id).Distinct().Count() != list.Length) throw new InvalidDataException("Duplicate or excessive requirements.");
            foreach (var requirement in list)
            {
                if (requirement.Id == owner || !ids.Contains(requirement.Id)) throw new InvalidDataException("Unknown or self dependency/conflict: " + requirement.Id);
                var min = Normalize(requirement.Minimum);
                if (requirement.Before is not null && Normalize(requirement.Before) <= min) throw new InvalidDataException("Empty dependency version range.");
            }
        }
    }
    public static void Validate(IEnumerable<PackageManifest> enabled)
    {
        var packages = enabled.ToDictionary(p => p.Id);
        foreach (var package in packages.Values)
        {
            foreach (var requirement in package.Dependencies ?? [])
                if (!packages.TryGetValue(requirement.Id, out var dependency) || !Matches(requirement, dependency.Version))
                    throw new InvalidOperationException(package.Name + " requires " + Name(requirement.Id) + " " + Range(requirement) + " to be installed and enabled.");
            foreach (var conflict in package.Conflicts ?? [])
                if (packages.TryGetValue(conflict.Id, out var other) && Matches(conflict, other.Version))
                    throw new InvalidOperationException(package.Name + " cannot be enabled together with " + other.Name + " " + other.Version + ". "
                        + (other.Id == "schematic-tools" && Version.Parse(other.Version) < new Version(0,17,0)
                            ? "Use Split Schematic Tools to replace the legacy bundle first."
                            : "Disable " + other.Name + " before installing or enabling " + package.Name + "."));
        }
        var visited = new HashSet<string>(); var active = new HashSet<string>();
        void Visit(string id)
        {
            if (active.Contains(id)) throw new InvalidDataException("Dependency cycle detected: " + id);
            if (!visited.Add(id)) return;
            active.Add(id); foreach (var dependency in packages[id].Dependencies ?? []) Visit(dependency.Id); active.Remove(id);
        }
        foreach (var id in packages.Keys) Visit(id);
    }
    private static string Name(string id) => Catalog.Approvals.Mods.FirstOrDefault(m => m.Id == id)?.Name ?? id;
    public static string Range(ModRequirement requirement) => requirement.Minimum + (requirement.Before is null ? " or newer" : " to before " + requirement.Before);
    public static ModPackage[] Resolve(IEnumerable<(ModPackage Package, bool Enabled)> existing, ModPackage selected, IEnumerable<ModPackage> available)
        => Resolve(existing, [selected], available);
    public static ModPackage[] Resolve(IEnumerable<(ModPackage Package, bool Enabled)> existing, IEnumerable<ModPackage> selected, IEnumerable<ModPackage> available)
    {
        var installed = existing.ToArray(); var requested = selected.ToArray();
        var all = available.Concat(installed.Select(p => p.Package)).Concat(requested).DistinctBy(p => p.ArchiveHash).ToDictionary(p => p.ArchiveHash);
        return ResolveChoices(installed.Where(p => p.Enabled).Select(p => DependencyChoice.From(p.Package)), requested.Select(DependencyChoice.From), all.Values.Select(DependencyChoice.From))
            .Select(c => all[c.Key]).ToArray();
    }
    public static DependencyChoice[] ResolveChoices(IEnumerable<DependencyChoice> enabled, IEnumerable<DependencyChoice> selected, IEnumerable<DependencyChoice> available)
    {
        var requested = selected.ToArray();
        if (requested.Length == 0 || requested.Select(p => p.Id).Distinct().Count() != requested.Length) throw new InvalidOperationException("Select one version of each mod to install.");
        var fixedIds = requested.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var start = enabled.ToDictionary(p => p.Id);
        foreach (var item in requested) start[item.Id] = item;
        var candidates = available.Concat(start.Values).Concat(requested).DistinctBy(p => p.Key).ToArray();
        string problem = "The selected mod versions cannot be installed together.";
        var visited = new HashSet<string>(); int attempts = 0;
        Dictionary<string, DependencyChoice>? Search(Dictionary<string, DependencyChoice> choice)
        {
            if (++attempts > 512) throw new InvalidOperationException("Dependency resolution exceeded its limit. Choose a compatible profile explicitly.");
            var stamp = string.Join("|", choice.OrderBy(p => p.Key).Select(p => p.Value.Key));
            if (!visited.Add(stamp)) return null;
            var requirements = choice.Values.SelectMany(p => p.Dependencies).GroupBy(r => r.Id).ToArray();
            foreach (var group in requirements)
            {
                if (choice.TryGetValue(group.Key, out var current) && group.All(r => Matches(r, current.Version))) continue;
                string needed = string.Join("; ", choice.Values.SelectMany(p => p.Dependencies.Where(r => r.Id == group.Key).Select(r => p.Name + " requires " + Name(r.Id) + " " + Range(r))));
                if (fixedIds.Contains(group.Key)) { problem = needed + ". Choose a different version of " + Name(group.Key) + "."; return null; }
                var matches = candidates.Where(p => p.Id == group.Key && group.All(r => Matches(r,p.Version))).OrderByDescending(p => Normalize(p.Version)).ToArray();
                if (matches.Length == 0) problem = needed + ". Refresh the catalog or import a package that satisfies these requirements.";
                foreach (var candidate in matches)
                {
                    var next = new Dictionary<string, DependencyChoice>(choice) { [group.Key] = candidate };
                    if (Search(next) is { } result) return result;
                }
                return null;
            }
            try { Validate(choice.Values.Select(p => p.Manifest)); return choice; } catch (InvalidOperationException error) { problem = error.Message; return null; }
        }
        return Search(start)?.Values.OrderBy(p => p.Id).ToArray() ?? throw new InvalidOperationException(problem);
    }
}
