using QdsModManager;

namespace QdsModManager.CatalogPublishing;

public static class Generator
{
    // Mod repositories supply bounded data only; their code is never executed.
    public static (CatalogDocument Index, PresentationDocument Presentation, SourcesDocument Sources) Merge(
        CatalogDocument existing, PresentationDocument presentation, SourcesDocument sources, IEnumerable<ReleaseRecord> incoming)
    {
        OnlineCatalog.Validate(existing); PresentationDocument.Validate(presentation);
        if (sources.Schema != 1 || sources.Releases.Select(r => (r.Id, r.Version)).Distinct().Count() != sources.Releases.Length)
            throw new InvalidDataException("Invalid catalog provenance.");
        var records = incoming.Select(Contract.Validate).OrderBy(r => r.Content.Id, StringComparer.Ordinal).ThenBy(r => Version.Parse(r.Release.Version)).ToArray();
        if (records.Select(r => (r.Content.Id, r.Release.Version)).Distinct().Count() != records.Length)
            throw new InvalidDataException("Duplicate release record.");
        var items = existing.Mods.ToDictionary(m => m.Id);
        var presentations = presentation.Mods.ToDictionary(m => m.Id);
        var receipts = sources.Releases.ToDictionary(r => (r.Id, r.Version));
        foreach (var record in records)
        {
            var id = record.Content.Id; var version = record.Release.Version;
            var receipt = new SourceReceipt(id, version, record.Repository, record.Commit, record.Tag, Data.Hash(Data.Bytes(record)));
            if (receipts.TryGetValue((id, version), out var approved) && approved != receipt)
                throw new InvalidDataException("An approved release record changed: " + id + " " + version);
            var previous = items.GetValueOrDefault(id);
            var releases = previous?.Releases.ToDictionary(r => r.Version) ?? [];
            if (releases.TryGetValue(version, out var pinned) && pinned != record.Release)
                throw new InvalidDataException("An existing version pin would change: " + id + " " + version);
            releases[version] = record.Release;
            // An older release arriving late must not roll current presentation back.
            bool latest = releases.Values.All(r => Version.Parse(r.Version) <= Version.Parse(version));
            items[id] = new(id, record.Content.Name, latest ? record.Content.Description : previous!.Description,
                record.Repository, releases.Values.OrderBy(r => Version.Parse(r.Version)).ToArray());
            var oldPresentation = presentations.GetValueOrDefault(id);
            var notes = oldPresentation?.ReleaseNotes.ToDictionary(p => p.Key, p => p.Value) ?? [];
            notes[version] = record.ReleaseNotes;
            presentations[id] = latest || oldPresentation is null
                ? new(id, record.Content.Author, Contract.ThumbnailUrl(record), record.Content.HelpUrl, notes.OrderBy(p => Version.Parse(p.Key)).ToDictionary(p => p.Key, p => p.Value), record.ThumbnailSha256)
                : oldPresentation with { ReleaseNotes = notes.OrderBy(p => Version.Parse(p.Key)).ToDictionary(p => p.Key, p => p.Value) };
            receipts[(id, version)] = receipt;
        }
        var index = OnlineCatalog.Validate(new(1, items.Values.OrderBy(m => m.Id, StringComparer.Ordinal).ToArray()));
        var result = PresentationDocument.Validate(new(1, presentations.Values.OrderBy(m => m.Id, StringComparer.Ordinal).ToArray()));
        if (Data.Bytes(index).Length > 1024 * 1024 || Data.Bytes(result).Length > 1024 * 1024)
            throw new InvalidDataException("Generated catalog exceeds manager download limits.");
        return (index, result, new(1, receipts.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ThenBy(r => Version.Parse(r.Version)).ToArray()));
    }
    public static void Write(string directory, (CatalogDocument Index, PresentationDocument Presentation, SourcesDocument Sources) result)
    {
        // Validation completes before any file changes; workflows commit all three together.
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "index.json"), Data.Bytes(result.Index));
        File.WriteAllBytes(Path.Combine(directory, "presentation.json"), Data.Bytes(result.Presentation));
        File.WriteAllBytes(Path.Combine(directory, "sources.json"), Data.Bytes(result.Sources));
    }
}
