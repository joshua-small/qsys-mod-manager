using System.IO.Compression;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QdsModManager;

public sealed record ManagerRelease(string Version, string Tag, string Url, string Sha256, long Size)
{
    public const string Repository = "joshua-small/qsys-mod-manager";
    public static Version Normalize(string value) { var v = System.Version.Parse(value); return new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision)); }
    public bool IsNewerThan(string current) => Normalize(Version) > Normalize(current);
    public void Validate()
    {
        if (!Regex.IsMatch(Tag, @"^v[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$") || Tag != "v" + Version || !System.Version.TryParse(Version, out _) || Size is < 1 or > 32 * 1024 * 1024)
            throw new InvalidDataException("Unsupported manager release.");
        Data.CheckHash(Sha256);
        if (!new[] { "Q-SYS-Mod-Manager", "QDS-Mod-Manager" }.Any(prefix => Url == $"https://github.com/{Repository}/releases/download/{Tag}/{prefix}-{Version}-win-x64.zip")) throw new InvalidDataException("Unexpected manager download address.");
    }
    internal static ManagerRelease Parse(byte[] bytes, string? expectedUrl = null)
    {
        using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) throw new InvalidDataException("Only stable published updates are supported.");
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith('v')) throw new InvalidDataException("Unsupported release tag.");
        string version = tag[1..];
        string name = expectedUrl is not null ? new Uri(expectedUrl).Segments.Last() : root.GetProperty("assets").EnumerateArray().Any(a => a.GetProperty("name").GetString() == $"Q-SYS-Mod-Manager-{version}-win-x64.zip") ? $"Q-SYS-Mod-Manager-{version}-win-x64.zip" : $"QDS-Mod-Manager-{version}-win-x64.zip";
        var asset = root.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == name);
        string digest = asset.GetProperty("digest").GetString() ?? "";
        if (!digest.StartsWith("sha256:", StringComparison.Ordinal)) throw new InvalidDataException("The release has no SHA-256 digest.");
        var release = new ManagerRelease(version, tag, asset.GetProperty("browser_download_url").GetString() ?? "", digest[7..], asset.GetProperty("size").GetInt64());
        release.Validate(); return release;
    }
}

public sealed class ManagerReleaseClient : IDisposable
{
    public const string Api = "https://api.github.com/repos/" + ManagerRelease.Repository + "/releases/";
    private readonly HttpClient client;
    public ManagerReleaseClient(HttpClient? client = null)
    {
        this.client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(35) };
        this.client.DefaultRequestHeaders.UserAgent.ParseAdd("Q-SYS-Mod-Manager-Updater/0.4");
    }
    public async Task<ManagerRelease> Latest(CancellationToken cancellation) => ManagerRelease.Parse(await Read(Api + "latest", 512 * 1024, cancellation));
    public async Task VerifyPublished(ManagerRelease release, CancellationToken cancellation)
    {
        release.Validate(); var published = ManagerRelease.Parse(await Read(Api + "tags/" + release.Tag, 512 * 1024, cancellation), release.Url);
        if (published != release) throw new InvalidDataException("The published update changed. Check for updates again.");
    }
    public async Task<byte[]> Download(ManagerRelease release, CancellationToken cancellation)
    {
        release.Validate(); byte[] bytes = await Read(release.Url, (int)release.Size, cancellation);
        _ = ManagerArchive.Open(bytes, release); return bytes;
    }
    private async Task<byte[]> Read(string address, int limit, CancellationToken cancellation)
    {
        Uri uri = new(address);
        for (int count = 0; count < 5; count++)
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect or HttpStatusCode.SeeOther)
            {
                var location = response.Headers.Location ?? throw new IOException("Missing update redirect."); uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com")) throw new IOException("Update redirected outside GitHub release hosts.");
                continue;
            }
            if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429) throw new IOException("GitHub temporarily limited update checks. Try again later.");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Update download is larger than expected.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellation); using var output = new MemoryStream(); byte[] buffer = new byte[8192]; int read;
            while ((read = await input.ReadAsync(buffer, cancellation)) > 0) { if (output.Length + read > limit) throw new InvalidDataException("Update exceeds its download limit."); output.Write(buffer, 0, read); }
            return output.ToArray();
        }
        throw new IOException("Too many update redirects.");
    }
    public void Dispose() => client.Dispose();
}

public sealed class ManagerArchive
{
    public static readonly string[] Names = AppIdentity.Files(AppIdentity.Name);
    public static readonly string[] KnownNames = AppIdentity.Stems.SelectMany(AppIdentity.Files).Distinct().ToArray();
    public Dictionary<string, byte[]> Files { get; }
    private ManagerArchive(Dictionary<string, byte[]> files) => Files = files;
    public static ManagerArchive Open(byte[] bytes, ManagerRelease release)
    {
        release.Validate();
        if (bytes.LongLength != release.Size || Data.Hash(bytes) != release.Sha256) throw new InvalidDataException("The manager download did not match the published release.");
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        string stem = AppIdentity.Stems.SingleOrDefault(s => zip.Entries.Select(e => e.FullName).Order().SequenceEqual(AppIdentity.Files(s).Order())) ?? throw new InvalidDataException("Unexpected manager archive contents.");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            if (entry.Length > 8 * 1024 * 1024) throw new InvalidDataException("Oversized manager file.");
            using var stream = entry.Open(); using var output = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
            while ((count = stream.Read(buffer)) > 0) { if (output.Length + count > 8 * 1024 * 1024) throw new InvalidDataException("Manager archive expansion exceeded its limit."); output.Write(buffer, 0, count); }
            files.Add(entry.FullName, output.ToArray());
        }
        VerifyAssembly(files[stem + ".dll"], stem, release.Version);
        VerifyAssembly(files["QdsModManager.Core.dll"], "QdsModManager.Core", null);
        using (var pe = new PEReader(new MemoryStream(files[stem + ".exe"])))
            if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64) throw new InvalidDataException("The manager update is not a Windows x64 app.");
        using (var runtime = JsonDocument.Parse(files[stem + ".runtimeconfig.json"]))
            if (runtime.RootElement.GetProperty("runtimeOptions").GetProperty("tfm").GetString() != "net10.0") throw new InvalidDataException("This update needs a different .NET runtime. Use the release download instead.");
        using var dependencies = JsonDocument.Parse(files[stem + ".deps.json"]);
        return new(files);
    }
    internal static string VerifyAssembly(byte[] bytes, string expectedName, string? version)
    {
        using var pe = new PEReader(new MemoryStream(bytes)); var metadata = pe.GetMetadataReader(); var definition = metadata.GetAssemblyDefinition();
        if (metadata.GetString(definition.Name) != expectedName || version is not null && definition.Version != ManagerRelease.Normalize(version)) throw new InvalidDataException("Manager assembly identity does not match the update.");
        return definition.Version.ToString();
    }
}
