using System.Text;
using System.Text.RegularExpressions;
using QdsModManager;

namespace QdsModManager.CatalogPublishing;

public sealed record ModContent(int Schema, string Id, string Name, string Description, string Author, string Thumbnail, string HelpUrl);
public sealed record ReleaseRecord(int Schema, string Repository, string Commit, string Tag, ModContent Content,
    CatalogRelease Release, string ReleaseNotes, string ThumbnailSha256);
public sealed record SourceReceipt(string Id, string Version, string Repository, string Commit, string Tag, string RecordSha256);
public sealed record SourcesDocument(int Schema, SourceReceipt[] Releases);

public static partial class Contract
{
    public const string AssetName = "qmm-catalog.json";
    public const int RecordLimit = 64 * 1024, ImageLimit = 1024 * 1024;
    [GeneratedRegex(@"^v?[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$")] private static partial Regex TagPattern();
    [GeneratedRegex(@"^[0-9a-f]{40}$")] private static partial Regex CommitPattern();
    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$")] private static partial Regex AuthorPattern();
    [GeneratedRegex(@"^catalog/[A-Za-z0-9_./-]+\.(png|jpg|jpeg|ico)$")] private static partial Regex ImagePattern();
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    public static void Identity(string repository, string tag, string commit)
    {
        if (!Catalog.Mods.Any(m => OnlineCatalog.Repository(m.Id) == repository) || !TagPattern().IsMatch(tag) || !CommitPattern().IsMatch(commit))
            throw new InvalidDataException("Unapproved repository, invalid release tag or non-immutable content commit.");
    }
    public static void Content(ModContent content, string repository)
    {
        var mod = Catalog.Mod(content.Id);
        if (content.Schema != 1 || content.Name != mod.Name || OnlineCatalog.Repository(content.Id) != repository
            || string.IsNullOrWhiteSpace(content.Description) || content.Description.Length > 4000
            || !AuthorPattern().IsMatch(content.Author) || content.Author.Contains("--")
            || !ImagePattern().IsMatch(content.Thumbnail) || content.Thumbnail.Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException("Invalid repository-owned mod content.");
        PresentationDocument.Validate(new(1, [new(content.Id, content.Author,
            "https://raw.githubusercontent.com/" + repository + "/main/" + content.Thumbnail, content.HelpUrl, [])]));
    }
    public static ReleaseRecord Validate(ReleaseRecord record)
    {
        Identity(record.Repository, record.Tag, record.Commit); Content(record.Content, record.Repository);
        Data.CheckHash(record.ThumbnailSha256);
        var release = record.Release;
        if (record.Schema != 1 || Normalize(Version.Parse(record.Tag.TrimStart('v'))).ToString() != release.Version
            || !PackageCompatibility.LabelMatches(release.DesignerVersion, release.HostSha256, release.CommonSha256)
            || string.IsNullOrWhiteSpace(record.ReleaseNotes) || record.ReleaseNotes.Length > 12000)
            throw new InvalidDataException("Release version, notes or exact Designer build is invalid.");
        var prefix = "https://github.com/" + record.Repository + "/releases/download/" + record.Tag + "/";
        if (!release.Url.StartsWith(prefix, StringComparison.Ordinal) || release.Url[prefix.Length..].Contains('/'))
            throw new InvalidDataException("Package is outside the declared release.");
        OnlineCatalog.Validate(new(1, [new(record.Content.Id, record.Content.Name, record.Content.Description, record.Repository, [release])]));
        if (Data.Bytes(record).Length > RecordLimit) throw new InvalidDataException("Release record is too large.");
        return record;
    }
    public static string ThumbnailUrl(ReleaseRecord record) => "https://raw.githubusercontent.com/" + record.Repository + "/" + record.Commit + "/" + record.Content.Thumbnail;
    public static byte[] Read(string path, int limit)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > limit) throw new InvalidDataException("Missing or oversized source file: " + path);
        return File.ReadAllBytes(path);
    }
    public static byte[] Image(byte[] bytes)
    {
        if (bytes.Length < 8 || bytes.Length > ImageLimit ||
            !(bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137, 80, 78, 71, 13, 10, 26, 10}) ||
              (bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) ||
              bytes.AsSpan(0, 4).SequenceEqual(new byte[] {0, 0, 1, 0})))
            throw new InvalidDataException("Thumbnail must be a bounded PNG, JPEG or ICO image.");
        return bytes;
    }
    public static void Package(ReleaseRecord record, string path)
    {
        Validate(record); var package = ModPackage.Open(path);
        if (package.Manifest.Id != record.Content.Id || package.ArchiveHash != record.Release.Sha256 ||
            new FileInfo(path).Length != record.Release.Size || package.Manifest.Version != record.Release.Version ||
            package.Manifest.HostSha256 != record.Release.HostSha256 || package.Manifest.CommonSha256 != record.Release.CommonSha256)
            throw new InvalidDataException("Package differs from its release record.");
    }
    public static ReleaseRecord Publish(string repository, string tag, string commit, string source, string packagePath, string assetFile)
    {
        Identity(repository, tag, commit);
        var content = Data.Read<ModContent>(Read(Path.Combine(source, "catalog", "mod.json"), RecordLimit)); Content(content, repository);
        var notes = new UTF8Encoding(false, true).GetString(Read(Path.Combine(source, "catalog", "releases", tag + ".md"), RecordLimit)).Trim();
        var image = Image(Read(Path.Combine(source, content.Thumbnail), ImageLimit));
        var package = ModPackage.Open(packagePath);
        if (Path.GetFileName(assetFile) != assetFile || !Regex.IsMatch(assetFile, @"^[A-Za-z0-9_.-]+\.qdsmod$"))
            throw new InvalidDataException("Invalid package asset filename.");
        var record = new ReleaseRecord(1, repository, commit, tag, content,
            new(package.Manifest.Version, PackageCompatibility.Legacy(package.Manifest.HostSha256, package.Manifest.CommonSha256) ? "10.5" : PackageCompatibility.Version(package.Manifest.HostSha256, package.Manifest.CommonSha256)!, package.Manifest.HostSha256, package.Manifest.CommonSha256,
                "https://github.com/" + repository + "/releases/download/" + tag + "/" + assetFile,
                package.ArchiveHash, new FileInfo(packagePath).Length), notes, Data.Hash(image));
        Package(record, packagePath); return record;
    }
}
