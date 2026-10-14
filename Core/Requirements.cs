namespace QdsModManager;

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
                    throw new InvalidOperationException(package.Name + " requires enabled " + requirement.Id + " " + requirement.Minimum + (requirement.Before is null ? " or newer." : " to before " + requirement.Before + "."));
            foreach (var conflict in package.Conflicts ?? [])
                if (packages.TryGetValue(conflict.Id, out var other) && Matches(conflict, other.Version))
                    throw new InvalidOperationException(package.Name + " conflicts with " + other.Name + " " + other.Version + ". Use the Schematic Tools migration for the legacy bundle.");
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
    public static ModPackage[] Resolve(IEnumerable<(ModPackage Package, bool Enabled)> existing, ModPackage selected, IEnumerable<ModPackage> available)
    {
        var start = existing.Where(p => p.Enabled).Select(p => p.Package).ToDictionary(p => p.Manifest.Id);
        start[selected.Manifest.Id] = selected;
        var candidates = available.Concat(start.Values).Append(selected).DistinctBy(p => p.ArchiveHash).ToArray();
        var visited = new HashSet<string>(); int attempts = 0;
        Dictionary<string, ModPackage>? Search(Dictionary<string, ModPackage> choice)
        {
            if (++attempts > 512) throw new InvalidOperationException("Dependency resolution exceeded its limit. Choose a compatible profile explicitly.");
            var stamp = string.Join("|", choice.OrderBy(p => p.Key).Select(p => p.Value.ArchiveHash));
            if (!visited.Add(stamp)) return null;
            var requirements = choice.Values.SelectMany(p => p.Manifest.Dependencies ?? []).GroupBy(r => r.Id).ToArray();
            foreach (var group in requirements)
            {
                if (choice.TryGetValue(group.Key, out var current) && group.All(r => Matches(r, current.Manifest.Version))) continue;
                if (group.Key == selected.Manifest.Id) return null;
                foreach (var candidate in candidates.Where(p => p.Manifest.Id == group.Key && group.All(r => Matches(r,p.Manifest.Version))).OrderByDescending(p => Normalize(p.Manifest.Version)))
                {
                    var next = new Dictionary<string, ModPackage>(choice) { [group.Key] = candidate };
                    if (Search(next) is { } result) return result;
                }
                return null;
            }
            try { Validate(choice.Values.Select(p => p.Manifest)); return choice; } catch (InvalidOperationException) { return null; }
        }
        return Search(start)?.Values.OrderBy(p => p.Manifest.Id).ToArray() ?? throw new InvalidOperationException("No compatible dependency set is available. Add the required packages or use the Schematic Tools migration to replace the legacy bundle.");
    }
}
