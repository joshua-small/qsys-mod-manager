namespace QdsModManager;

public sealed record ModPresentation(string Id, string Author, string? Thumbnail, string HelpUrl, Dictionary<string, string> ReleaseNotes);
public sealed record PresentationDocument(int Schema, ModPresentation[] Mods)
{
    public static PresentationDocument Bundled()
    {
        using var stream = typeof(PresentationDocument).Assembly.GetManifestResourceStream("QdsModManager.Presentation.json")!;
        using var memory = new MemoryStream(); stream.CopyTo(memory); return Validate(Data.Read<PresentationDocument>(memory.ToArray()));
    }
    public static PresentationDocument Validate(PresentationDocument document)
    {
        if (document.Schema != 1 || document.Mods.Length > Catalog.Mods.Length || document.Mods.Select(m => m.Id).Distinct().Count() != document.Mods.Length) throw new InvalidDataException("Invalid presentation metadata.");
        foreach (var mod in document.Mods)
        {
            Catalog.Mod(mod.Id);
            if (mod.Author.Length > 100 || mod.ReleaseNotes.Count > 100 || mod.ReleaseNotes.Any(r => !Version.TryParse(r.Key, out _) || r.Value.Length > 12000)) throw new InvalidDataException("Invalid mod presentation.");
            if (mod.HelpUrl != "https://github.com/" + OnlineCatalog.Repository(mod.Id) && !mod.HelpUrl.StartsWith("https://github.com/" + OnlineCatalog.Repository(mod.Id) + "/", StringComparison.Ordinal)) throw new InvalidDataException("Untrusted help link.");
            if (mod.Thumbnail is not null && !mod.Thumbnail.StartsWith("https://raw.githubusercontent.com/" + OnlineCatalog.Repository(mod.Id) + "/", StringComparison.Ordinal)) throw new InvalidDataException("Untrusted thumbnail.");
        }
        return document;
    }
}
