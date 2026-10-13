using System.Text;
using QdsModManager;
using QdsModManager.CatalogPublishing;

try
{
    if (args.Length == 4 && args[0] == "check")
    {
        var proposedSources = Sources(args[1]);
        var previous = ReadCatalog(args[3]);
        var records = new List<ReleaseRecord>();
        using var github = new GitHub();
        foreach (var source in proposedSources.Releases) records.Add(await github.Approved(source, args[2]));
        if (previous.Sources.Releases.Any(s => !proposedSources.Releases.Contains(s)))
            throw new InvalidDataException("An approved provenance entry was removed or changed.");
        var expected = Generator.Merge(previous.Index, previous.Presentation, previous.Sources, records);
        if (!Data.Bytes(expected.Index).SequenceEqual(File.ReadAllBytes(Path.Combine(args[1], "index.json"))) ||
            !Data.Bytes(expected.Presentation).SequenceEqual(File.ReadAllBytes(Path.Combine(args[1], "presentation.json"))) ||
            !Data.Bytes(expected.Sources).SequenceEqual(File.ReadAllBytes(Path.Combine(args[1], "sources.json"))))
            throw new InvalidDataException("Catalog differs from generated approved sources or removes historical data.");
        Console.WriteLine(records.Count + " approved sources verified; generated files reproduce exactly."); return;
    }
    if (args.Length == 3 && args[0] == "source")
    {
        var content = Data.Read<ModContent>(Contract.Read(Path.Combine(args[2], "catalog/mod.json"), Contract.RecordLimit));
        Contract.Content(content, args[1]); Contract.Image(Contract.Read(Path.Combine(args[2], content.Thumbnail), Contract.ImageLimit));
        var notes = Directory.GetFiles(Path.Combine(args[2], "catalog/releases"), "*.md");
        if (notes.Length == 0) throw new InvalidDataException("At least one version's release notes are required.");
        foreach (var path in notes)
        {
            Contract.Identity(args[1], Path.GetFileNameWithoutExtension(path), new string('0', 40));
            string text = new UTF8Encoding(false, true).GetString(Contract.Read(path, Contract.RecordLimit)).Trim();
            if (text.Length is < 1 or > 12000) throw new InvalidDataException("Invalid release notes.");
        }
        Console.WriteLine("Repository-owned metadata, artwork and release notes validated."); return;
    }
    if (args.Length == 8 && args[0] == "publish")
    {
        var record = Contract.Publish(args[1], args[2], args[3], args[4], args[5], args[6]);
        File.WriteAllBytes(args[7], Data.Bytes(record)); Console.WriteLine("Verified release record generated."); return;
    }
    if (args.Length == 6 && args[0] == "release")
    {
        using var github = new GitHub(); var record = await github.Prepare(args[1], args[2], args[3], args[4], args[5]);
        Console.WriteLine("Verified " + record.Content.Id + " " + record.Release.Version + "; existing package unchanged."); return;
    }
    if (args.Length == 3 && args[0] == "sync")
    {
        string directory = args[1];
        var index = Data.Read<CatalogDocument>(Contract.Read(Path.Combine(directory, "index.json"), 256 * 1024));
        var presentation = Data.Read<PresentationDocument>(Contract.Read(Path.Combine(directory, "presentation.json"), 256 * 1024));
        var path = Path.Combine(directory, "sources.json");
        var sources = File.Exists(path) ? Data.Read<SourcesDocument>(Contract.Read(path, 256 * 1024)) : new(1, []);
        using var github = new GitHub(); var records = await github.Collect(args[2]);
        var result = Generator.Merge(index, presentation, sources, records);
        // An approved record disappearing must not silently erase or unverify its pin.
        if (sources.Releases.Any(s => !records.Any(r => r.Content.Id == s.Id && r.Release.Version == s.Version)))
            throw new InvalidDataException("An approved release record is no longer available.");
        Generator.Write(directory, result);
        Console.WriteLine(records.Length + " release records verified; historical pins retained."); return;
    }
    throw new ArgumentException("Usage: source <repo> <source-dir> | publish <repo> <tag> <commit> <source-dir> <package> <asset-name> <output> | release <repo> <tag> <commit> <source-dir> <output-dir> | sync <catalog-dir> <cache-dir> | check <catalog-dir> <cache-dir> <baseline-catalog-dir>");
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message); Environment.ExitCode = 1;
}

static SourcesDocument Sources(string directory)
{
    string path = Path.Combine(directory, "sources.json");
    return File.Exists(path) ? Data.Read<SourcesDocument>(Contract.Read(path, 256 * 1024)) : new(1, []);
}
static (CatalogDocument Index, PresentationDocument Presentation, SourcesDocument Sources) ReadCatalog(string directory) =>
    (Data.Read<CatalogDocument>(Contract.Read(Path.Combine(directory, "index.json"), 256 * 1024)),
     Data.Read<PresentationDocument>(Contract.Read(Path.Combine(directory, "presentation.json"), 256 * 1024)), Sources(directory));
