using System.Net;

namespace QdsModManager;

public sealed record CatalogRelease(string Version, string DesignerVersion, string HostSha256, string CommonSha256, string Url, string Sha256, long Size);
public sealed record CatalogItem(string Id, string Name, string Description, string Repository, CatalogRelease[] Releases);
public sealed record CatalogDocument(int Schema, CatalogItem[] Mods);
public sealed record CatalogSnapshot(DateTimeOffset FetchedUtc, CatalogDocument Document, bool Offline = false);

public sealed class OnlineCatalog : IDisposable
{
    public const string IndexUrl = "https://raw.githubusercontent.com/joshua-small/qsys-mod-manager/main/catalog/index.json";
    private readonly HttpClient client;
    private readonly string cache;
    private const int IndexLimit = 256 * 1024, PackageLimit = 24 * 1024 * 1024;
    public OnlineCatalog(string dataDirectory, HttpClient? client = null)
    {
        this.client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
        this.client.DefaultRequestHeaders.UserAgent.ParseAdd("QDS-Mod-Manager/0.2");
        cache = Path.Combine(dataDirectory, "catalog-cache.json");
    }
    public static string Repository(string id) => "joshua-small/" + (id switch
    {
        "schematic-tools" => "qsys-schematic-tools", "automatic-geometry" => "qsys-automatic-geometry",
        _ => throw new InvalidDataException("This mod needs a newer manager.")
    });
    public static CatalogDocument Validate(CatalogDocument document)
    {
        if (document.Schema != 1 || document.Mods.Length > 100 || document.Mods.Select(m => m.Id).Distinct().Count() != document.Mods.Length)
            throw new InvalidDataException("Unsupported catalog.");
        foreach (var mod in document.Mods)
        {
            if (mod.Name != Catalog.Mod(mod.Id).Name || mod.Repository != Repository(mod.Id) || mod.Description.Length > 4000 || mod.Releases.Length is < 1 or > 100
                || mod.Releases.Select(r => r.Version).Distinct().Count() != mod.Releases.Length) throw new InvalidDataException("Invalid catalog entry.");
            foreach (var release in mod.Releases)
            {
                if (!Version.TryParse(release.Version, out var version) || version.Build < 0 || release.DesignerVersion.Length > 40 || release.Size is < 1 or > PackageLimit)
                    throw new InvalidDataException("Invalid catalog version.");
                Data.CheckHash(release.Sha256); Data.CheckHash(release.HostSha256); Data.CheckHash(release.CommonSha256);
                if (!Uri.TryCreate(release.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || !uri.IsDefaultPort
                    || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
                    || !uri.AbsolutePath.StartsWith("/" + mod.Repository + "/releases/download/", StringComparison.Ordinal)
                    || !uri.AbsolutePath.EndsWith(".qdsmod", StringComparison.Ordinal) || release.Url.Contains('%')) throw new InvalidDataException("Untrusted package address.");
            }
        }
        return document;
    }
    public static bool Compatible(CatalogRelease release, string? host, string? common) =>
        release.HostSha256 == Catalog.HostHash && release.CommonSha256 == Catalog.CommonHash && release.HostSha256 == host && release.CommonSha256 == common;
    public static bool Matches(CatalogItem item, string query) => query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .All(term => (item.Name + " " + item.Description).Contains(term, StringComparison.OrdinalIgnoreCase));
    public async Task<CatalogSnapshot> Fetch(CancellationToken cancellation)
    {
        try
        {
            var bytes = await Read(new Uri(IndexUrl), IndexLimit, cancellation);
            var document = Validate(Data.Read<CatalogDocument>(bytes));
            var snapshot = new CatalogSnapshot(DateTimeOffset.UtcNow, document);
            Save(cache, Data.Bytes(snapshot)); return snapshot;
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellation.IsCancellationRequested)
        {
            if (!File.Exists(cache) || new FileInfo(cache).Length > IndexLimit) throw;
            var snapshot = Data.Read<CatalogSnapshot>(File.ReadAllBytes(cache)); Validate(snapshot.Document);
            return snapshot with { Offline = true };
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
    public async Task<PresentationDocument?> FetchPresentation(CancellationToken cancellation)
    {
        string path = Path.Combine(Path.GetDirectoryName(cache)!, "presentation-cache.json");
        try
        {
            var bytes = await Read(new Uri(IndexUrl.Replace("index.json", "presentation.json")), IndexLimit, cancellation);
            var document = PresentationDocument.Validate(Data.Read<PresentationDocument>(bytes)); Save(path, bytes); return document;
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellation.IsCancellationRequested)
        {
            try { return File.Exists(path) && new FileInfo(path).Length <= IndexLimit ? PresentationDocument.Validate(Data.Read<PresentationDocument>(File.ReadAllBytes(path))) : null; } catch { return null; }
        }
    }
    public async Task<byte[]?> Thumbnail(ModPresentation mod, CancellationToken cancellation)
    {
        PresentationDocument.Validate(new(1, [mod])); if (mod.Thumbnail is null) return null;
        string path = Path.Combine(Path.GetDirectoryName(cache)!, "thumbnails", Data.Hash(System.Text.Encoding.UTF8.GetBytes(mod.Thumbnail)));
        try { var bytes = await Read(new Uri(mod.Thumbnail), 1024 * 1024, cancellation); Save(path, bytes); return bytes; }
        catch (Exception error) when (error is not OperationCanceledException || !cancellation.IsCancellationRequested) { return File.Exists(path) && new FileInfo(path).Length <= 1024 * 1024 ? File.ReadAllBytes(path) : null; }
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
    public void Dispose() => client.Dispose();
}
