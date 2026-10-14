using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace QdsModManager;

public sealed record ApprovedFile(string Name, bool Shared, string? Sha256 = null);
public sealed record ModRequirement(string Id, string Minimum, string? Before = null);
public sealed record PackageLayout(int Schema, ApprovedFile[] Files,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ModRequirement[]? Dependencies = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ModRequirement[]? Conflicts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MinimumVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BeforeVersion = null);
public sealed record ModDefinition(string Id, string Name, string Repository, string Module, string? LegacyMarker, PackageLayout[] Layouts)
{
    [JsonIgnore] public string Description => Catalog.Current.Mods.FirstOrDefault(m => m.Id == Id)?.Description ?? "";
    // Only historical schema-1 packages use this file; schema 2 has no activation marker.
    [JsonIgnore] public string Marker => LegacyMarker ?? throw new InvalidDataException("This mod does not use an enable marker.");
}
public sealed record ApprovalDocument(int Schema, ModDefinition[] Mods)
{
    public ModDefinition Mod(string id) => Mods.SingleOrDefault(m => m.Id == id) ?? throw new InvalidDataException("This mod has not been approved in the catalog.");
    [JsonIgnore] public string[] FileNames => Mods.SelectMany(m => m.Layouts).SelectMany(l => l.Files).Select(f => f.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    public void FileName(string name) { if (!Mods.Any(m => m.Layouts.Any(l => l.Files.Any(f => f.Name == name)))) throw new InvalidDataException("Unapproved package file: " + name); }
    public static ApprovalDocument Validate(ApprovalDocument document)
    {
        if (document.Schema != 1 || document.Mods.Length is < 1 or > 256 || document.Mods.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.Mods.Length
            || document.Mods.Select(m => m.Repository).Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.Mods.Length)
            throw new InvalidDataException("Invalid catalog approvals.");
        var owners = new Dictionary<string, (string Id, ApprovedFile File)>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in document.Mods)
        {
            if (!Regex.IsMatch(mod.Id, "^[a-z][a-z0-9-]{0,63}$") || string.IsNullOrWhiteSpace(mod.Name) || mod.Name.Length > 100
                || !Regex.IsMatch(mod.Repository, "^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}$")
                || !Regex.IsMatch(mod.Module, @"^Designer\.Modules\.[A-Za-z0-9_-]+\.dll$") || mod.Layouts.Length is < 1 or > 16
                || new[] { "Designer.Modules.SearchAndReplace.dll", "Designer.Modules.QSysVersionManagerModule.dll", "Designer.Modules.TextDocumentModule.dll" }.Contains(mod.Module, StringComparer.OrdinalIgnoreCase)
                || (mod.LegacyMarker is not null && mod.LegacyMarker != "." + mod.Id + "-enabled")) throw new InvalidDataException("Invalid approved mod identity.");
            foreach (var layout in mod.Layouts)
            {
                if (layout.Schema is not (1 or 2 or 3) || layout.Files.Length is < 1 or > 32
                    || layout.Files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != layout.Files.Length
                    || layout.Files.Count(f => f.Name == mod.Module && !f.Shared && f.Sha256 is null) != 1
                    || (layout.Schema == 1 && (mod.LegacyMarker is null || !layout.Files.Any(f => f.Name == mod.LegacyMarker))))
                    throw new InvalidDataException("Invalid approved file layout.");
                Requirements.ValidatePolicy(mod.Id, layout.Schema, layout.Dependencies, layout.Conflicts, document.Mods.Select(m => m.Id));
                if (layout.Schema < 3 ? layout.MinimumVersion is not null || layout.BeforeVersion is not null : layout.MinimumVersion is null)
                    throw new InvalidDataException("Versioned layouts require schema 3 and a minimum version.");
                if (layout.MinimumVersion is not null && !Requirements.Matches(new(mod.Id, layout.MinimumVersion, layout.BeforeVersion), layout.MinimumVersion))
                    throw new InvalidDataException("Empty package-layout version range.");
                foreach (var file in layout.Files)
                {
                    // Flat, namespaced additions only. Catalog approval cannot authorize vendor files, paths or scripts.
                    bool legacy = layout.Schema == 1 && file.Name == mod.LegacyMarker && !file.Shared;
                    bool shared = file.Shared && (file.Name is Catalog.Harmony or Catalog.License || file.Name.StartsWith("Qmm.Shared.", StringComparison.Ordinal));
                    bool owned = !file.Shared && (file.Name == mod.Module || file.Name.StartsWith("Qmm." + mod.Id + ".", StringComparison.Ordinal));
                    if (!(legacy || shared || owned) || !Regex.IsMatch(file.Name, @"^[A-Za-z0-9_.-]{1,160}$") || file.Name.Contains("..")
                        || file.Name.EndsWith('.') || file.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        || (layout.Schema >= 2 && file.Name == mod.LegacyMarker)) throw new InvalidDataException("Unsafe approved file target: " + file.Name);
                    if (file.Shared && file.Sha256 is null) throw new InvalidDataException("Shared files require an approved hash.");
                    if (file.Sha256 is not null) Data.CheckHash(file.Sha256);
                    if (owners.TryGetValue(file.Name, out var owner) && (owner.File != file || (owner.Id != mod.Id && !file.Shared)))
                        throw new InvalidDataException("Conflicting approved file ownership: " + file.Name);
                    owners[file.Name] = (mod.Id, file);
                }
            }
            foreach (var group in mod.Layouts.GroupBy(l => l.Schema + "|" + string.Join("|", l.Files.Select(f => f.Name).Order(StringComparer.Ordinal))))
            {
                var layouts = group.ToArray();
                for (int i = 0; i < layouts.Length; i++)
                    for (int j = i + 1; j < layouts.Length; j++)
                    {
                        var first = layouts[i]; var second = layouts[j];
                        if (first.MinimumVersion is null || second.MinimumVersion is null
                            || Requirements.Matches(new(mod.Id,first.MinimumVersion,first.BeforeVersion),second.MinimumVersion)
                            || Requirements.Matches(new(mod.Id,second.MinimumVersion,second.BeforeVersion),first.MinimumVersion))
                            throw new InvalidDataException("Ambiguous overlapping package layouts.");
                    }
            }
        }
        if (owners.Count > 1024) throw new InvalidDataException("Catalog file limit exceeded.");
        return document;
    }
    public static void Preserve(ApprovalDocument previous, ApprovalDocument next)
    {
        Validate(next);
        foreach (var old in previous.Mods)
        {
            var mod = next.Mod(old.Id);
            if (mod.Name != old.Name || mod.Repository != old.Repository || mod.Module != old.Module || mod.LegacyMarker != old.LegacyMarker
                || old.Layouts.Any(l => !mod.Layouts.Any(n => Data.Bytes(l).SequenceEqual(Data.Bytes(n)))))
                throw new InvalidDataException("Existing approval identities and file layouts must be retained for restoration.");
        }
    }
}

public sealed record CatalogFeed(int Schema, ApprovalDocument Approvals, CatalogDocument Index, PresentationDocument Presentation, string SourcesSha256);
public sealed record SignedCatalog(int Schema, string Payload, string Signature);
public static class CatalogTrust
{
    public const int Limit = 4 * 1024 * 1024;
    public static CatalogFeed Verify(SignedCatalog envelope)
    {
        using var key = ECDsa.Create();
        using var stream = typeof(CatalogTrust).Assembly.GetManifestResourceStream("QdsModManager.CatalogKey.pem")!;
        using var reader = new StreamReader(stream); key.ImportFromPem(reader.ReadToEnd());
        return Verify(envelope, key);
    }
    internal static CatalogFeed Verify(SignedCatalog envelope, ECDsa key)
    {
        if (envelope.Schema != 1 || envelope.Payload.Length > Limit || envelope.Signature.Length > 128) throw new InvalidDataException("Invalid signed catalog envelope.");
        byte[] payload = Convert.FromBase64String(envelope.Payload), signature = Convert.FromBase64String(envelope.Signature);
        if (!key.VerifyData(payload, signature, HashAlgorithmName.SHA256)) throw new InvalidDataException("Catalog signature did not verify.");
        var feed = Data.Read<CatalogFeed>(payload);
        if (feed.Schema != 1) throw new InvalidDataException("Unsupported catalog feed.");
        Data.CheckHash(feed.SourcesSha256);
        ApprovalDocument.Validate(feed.Approvals);
        OnlineCatalog.Validate(feed.Index, feed.Approvals); PresentationDocument.Validate(feed.Presentation, feed.Approvals);
        return feed;
    }
}
