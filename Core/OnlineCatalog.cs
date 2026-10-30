using System.Net;

namespace QdsModManager;

public sealed record CatalogRelease(string Version, string DesignerVersion, string HostSha256, string CommonSha256, string Url, string Sha256, long Size);
public sealed record CatalogItem(string Id, string Name, string Description, string Repository, CatalogRelease[] Releases);
public sealed record CatalogDocument(int Schema, CatalogItem[] Mods);
public sealed record CatalogSnapshot(DateTimeOffset FetchedUtc, CatalogDocument Document, bool Offline = false);

public sealed class OnlineCatalog : IDisposable
{
    public const string IndexUrl = "https://raw.githubusercontent.com/joshua-small/qsys-mod-catalog/published/catalog/feed.json";
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly string cache;
    private const int IndexLimit = CatalogTrust.Limit, PackageLimit = 24 * 1024 * 1024;
    public OnlineCatalog(string dataDirectory, HttpClient? client = null)
    {
        ownsClient = client is null;
        this.client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
        this.client.DefaultRequestHeaders.UserAgent.ParseAdd("QDS-Mod-Manager/0.2");
        cache = Path.Combine(dataDirectory, "signed-catalog-cache.json");
    }
    public static string Repository(string id) => Catalog.Mod(id).Repository;
    public static CatalogDocument Validate(CatalogDocument document, ApprovalDocument? approvals = null)
    {
        approvals ??= Catalog.Approvals;
        if (document.Schema != 1 || document.Mods.Length > 256 || document.Mods.Select(m => m.Id).Distinct().Count() != document.Mods.Length)
            throw new InvalidDataException("Unsupported catalog.");
        foreach (var mod in document.Mods)
        {
            if (mod.Name != approvals.Mod(mod.Id).Name || mod.Repository != approvals.Mod(mod.Id).Repository || mod.Description.Length > 4000 || mod.Releases.Length is < 1 or > 100
                || mod.Releases.Select(r => r.Version).Distinct().Count() != mod.Releases.Length) throw new InvalidDataException("Invalid catalog entry.");
            foreach (var release in mod.Releases)
            {
                if (!Version.TryParse(release.Version, out var version) || version.Build < 0 || release.DesignerVersion.Length > 40 || release.Size is < 1 or > PackageLimit)
                    throw new InvalidDataException("Invalid catalog version.");
                Data.CheckHash(release.Sha256); Data.CheckHash(release.HostSha256); Data.CheckHash(release.CommonSha256);
                if (!PackageCompatibility.LabelMatches(release.DesignerVersion, release.HostSha256, release.CommonSha256))
                    throw new InvalidDataException("Unrecognized exact Designer package target or mismatched version label.");
                if (!Uri.TryCreate(release.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || !uri.IsDefaultPort
                    || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
                    || !uri.AbsolutePath.StartsWith("/" + mod.Repository + "/releases/download/", StringComparison.Ordinal)
                    || !uri.AbsolutePath.EndsWith(".qdsmod", StringComparison.Ordinal) || release.Url.Contains('%')) throw new InvalidDataException("Untrusted package address.");
            }
        }
        return document;
    }
    public static bool Compatible(CatalogRelease release, string? host, string? common) =>
        PackageCompatibility.Exact(release.HostSha256, release.CommonSha256, host, common);
    public static bool Matches(CatalogItem item, string query) => query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .All(term => (item.Name + " " + item.Description).Contains(term, StringComparison.OrdinalIgnoreCase));
    public async Task<CatalogSnapshot> Fetch(CancellationToken cancellation)
    {
        try
        {
            var bytes = await Read(new Uri(IndexUrl), IndexLimit, cancellation);
            var proof = Data.Read<SignedCatalog>(bytes);
            var feed = CatalogTrust.Verify(proof); ApprovalDocument.Preserve(Catalog.Approvals, feed.Approvals);
            Save(cache, bytes); Catalog.Activate(proof);
            var snapshot = new CatalogSnapshot(DateTimeOffset.UtcNow, Catalog.Current);
            return snapshot;
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellation.IsCancellationRequested)
        {
            if (!File.Exists(cache) || new FileInfo(cache).Length > IndexLimit) throw;
            Catalog.Activate(Data.Read<SignedCatalog>(File.ReadAllBytes(cache)));
            return new(File.GetLastWriteTimeUtc(cache), Catalog.Current, true);
        }
    }
    public async Task<ModPackage> Download(CatalogItem item, CatalogRelease release, string directory, CancellationToken cancellation)
    {
        Validate(new(1, new[] { item }));
        if (!item.Releases.Contains(release)) throw new InvalidDataException("Version is not in the catalog.");
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, release.Sha256 + ".qdsmod");
        if (File.Exists(destination)) return Verify(destination, item, release);
        var bytes = await Read(new Uri(release.Url), (int)release.Size, cancellation);
        if (bytes.LongLength != release.Size || Data.Hash(bytes) != release.Sha256) throw new InvalidDataException("Download did not match the published package. Nothing was installed.");
        string staging = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".download");
        try
        {
            await File.WriteAllBytesAsync(staging, bytes, cancellation);
            var package = Verify(staging, item, release); cancellation.ThrowIfCancellationRequested();
            File.Move(staging, destination, false); return ModPackage.Open(destination);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
    public void LoadCached()
    {
        if (File.Exists(cache) && new FileInfo(cache).Length <= IndexLimit)
            Catalog.Activate(Data.Read<SignedCatalog>(File.ReadAllBytes(cache)));
    }
    public Task<PresentationDocument?> FetchPresentation(CancellationToken cancellation) => Task.FromResult(Catalog.Presentation);
    public async Task<byte[]?> Thumbnail(ModPresentation mod, CancellationToken cancellation)
    {
        PresentationDocument.Validate(new(1, [mod])); if (mod.Thumbnail is null) return null;
        string path = Path.Combine(Path.GetDirectoryName(cache)!, "thumbnails", Data.Hash(System.Text.Encoding.UTF8.GetBytes(mod.Thumbnail)));
        byte[] Verified(byte[] bytes) { if (mod.ThumbnailSha256 is not null && Data.Hash(bytes) != mod.ThumbnailSha256) throw new InvalidDataException("Artwork hash differs from its catalog record."); return bytes; }
        try { var bytes = Verified(await Read(new Uri(mod.Thumbnail), 1024 * 1024, cancellation)); Save(path, bytes); return bytes; }
        catch (Exception error) when (error is not OperationCanceledException || !cancellation.IsCancellationRequested)
        {
            try { return File.Exists(path) && new FileInfo(path).Length <= 1024 * 1024 ? Verified(File.ReadAllBytes(path)) : null; } catch { return null; }
        }
    }
    private static ModPackage Verify(string path, CatalogItem item, CatalogRelease release)
    {
        var package = ModPackage.Open(path);
        if (package.ArchiveHash != release.Sha256 || new FileInfo(path).Length != release.Size || package.Manifest.Id != item.Id
            || Version.Parse(package.Manifest.Version) != Version.Parse(release.Version) || package.Manifest.HostSha256 != release.HostSha256 || package.Manifest.CommonSha256 != release.CommonSha256)
            throw new InvalidDataException("Downloaded package differs from its catalog entry.");
        return package;
    }
    private async Task<byte[]> Read(Uri uri, int limit, CancellationToken cancellation)
    {
        for (int redirect = 0; redirect < 5; redirect++)
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect or HttpStatusCode.SeeOther)
            {
                var location = response.Headers.Location ?? throw new IOException("Download redirected without an address.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.Host is not ("github.com" or "release-assets.githubusercontent.com" or "raw.githubusercontent.com"))
                    throw new IOException("Download redirected outside the release hosts.");
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Download is larger than expected.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellation); using var output = new MemoryStream();
            var buffer = new byte[8192]; int count;
            while ((count = await input.ReadAsync(buffer, cancellation)) > 0)
            {
                if (output.Length + count > limit) throw new InvalidDataException("Download exceeded its size limit.");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        throw new IOException("Too many download redirects.");
    }
    private static void Save(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); string staging = path + "." + Guid.NewGuid().ToString("N");
        try { File.WriteAllBytes(staging, bytes); File.Move(staging, path, true); }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
    public void Dispose() { if (ownsClient) client.Dispose(); }
}
