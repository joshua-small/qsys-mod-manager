using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using QdsModManager;

namespace QdsModManager.CatalogPublishing;

public sealed class GitHub : IDisposable
{
    private readonly HttpClient client;
    public GitHub(HttpClient? client = null) => this.client = client ?? new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
    public async Task<byte[]> Read(string url, int limit)
    {
        var uri = new Uri(url);
        for (int redirects = 0; redirects < 5; redirects++)
        {
            if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                uri.Host is not ("api.github.com" or "github.com" or "raw.githubusercontent.com" or "release-assets.githubusercontent.com"))
                throw new InvalidDataException("Untrusted download host.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Qsys-Catalog-Publisher/1");
            // The workflow token is never forwarded to content or release-asset hosts.
            if (uri.Host == "api.github.com" && Environment.GetEnvironmentVariable("GH_TOKEN") is { Length: > 0 } token)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var target = response.Headers.Location ?? throw new IOException("Redirect without location.");
                uri = target.IsAbsoluteUri ? target : new Uri(uri, target); continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Oversized download.");
            await using var input = await response.Content.ReadAsStreamAsync(); using var output = new MemoryStream();
            var buffer = new byte[8192]; int count;
            while ((count = await input.ReadAsync(buffer)) > 0)
            {
                if (output.Length + count > limit) throw new InvalidDataException("Oversized download.");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        throw new IOException("Too many redirects.");
    }
    private async Task<JsonElement> Api(string path)
    {
        using var document = JsonDocument.Parse(await Read("https://api.github.com/repos/" + path, 4 * 1024 * 1024));
        return document.RootElement.Clone();
    }
    private static bool Stable(JsonElement release) => !release.GetProperty("draft").GetBoolean() && !release.GetProperty("prerelease").GetBoolean();
    public async Task<ReleaseRecord> Prepare(string repository, string tag, string commit, string source, string output)
    {
        Contract.Identity(repository, tag, commit);
        var release = await Api(repository + "/releases/tags/" + tag);
        if (!Stable(release) || release.GetProperty("tag_name").GetString() != tag) throw new InvalidDataException("Only published stable releases are eligible.");
        var assets = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString()!.EndsWith(".qdsmod", StringComparison.Ordinal)).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("Release must have exactly one .qdsmod asset.");
        var name = assets[0].GetProperty("name").GetString()!;
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9_.-]+\.qdsmod$")) throw new InvalidDataException("Invalid asset filename.");
        Directory.CreateDirectory(output);
        string path = Path.Combine(output, name);
        await File.WriteAllBytesAsync(path, await Read("https://github.com/" + repository + "/releases/download/" + tag + "/" + name, 24 * 1024 * 1024));
        var record = Contract.Publish(repository, tag, commit, source, path, name);
        await VerifySource(record);
        var previous = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == Contract.AssetName).ToArray();
        if (previous.Length > 1) throw new InvalidDataException("Duplicate release record assets.");
        if (previous.Length == 1)
        {
            var old = Data.Read<ReleaseRecord>(await Read(RecordUrl(repository, tag), Contract.RecordLimit));
            if (!Data.Bytes(Contract.Validate(old)).SequenceEqual(Data.Bytes(record)))
                throw new InvalidDataException("Release record already exists with different content; it cannot be replaced.");
        }
        File.WriteAllBytes(Path.Combine(output, Contract.AssetName), Data.Bytes(record)); return record;
    }
    public static string RecordUrl(string repository, string tag) => "https://github.com/" + repository + "/releases/download/" + tag + "/" + Contract.AssetName;
    public async Task<ReleaseRecord> Approved(SourceReceipt receipt, string cache)
    {
        Contract.Identity(receipt.Repository, receipt.Tag, receipt.Commit);
        var record = Contract.Validate(Data.Read<ReleaseRecord>(await Read(RecordUrl(receipt.Repository, receipt.Tag), Contract.RecordLimit)));
        if (record.Content.Id != receipt.Id || record.Release.Version != receipt.Version || record.Repository != receipt.Repository ||
            record.Tag != receipt.Tag || record.Commit != receipt.Commit || Data.Hash(Data.Bytes(record)) != receipt.RecordSha256)
            throw new InvalidDataException("Approved release provenance changed.");
        await VerifySource(record);
        using var packages = new OnlineCatalog(Path.Combine(cache, "catalog"));
        var item = new CatalogItem(record.Content.Id, record.Content.Name, record.Content.Description, record.Repository, [record.Release]);
        var package = await packages.Download(item, record.Release, Path.Combine(cache, "packages"), default);
        Contract.Package(record, package.Path); return record;
    }
    public async Task VerifySource(ReleaseRecord record)
    {
        Contract.Validate(record);
        var prefix = "https://raw.githubusercontent.com/" + record.Repository + "/" + record.Commit + "/";
        var content = Data.Read<ModContent>(await Read(prefix + "catalog/mod.json", Contract.RecordLimit));
        var notes = new UTF8Encoding(false, true).GetString(await Read(prefix + "catalog/releases/" + record.Tag + ".md", Contract.RecordLimit)).Trim();
        var image = Contract.Image(await Read(Contract.ThumbnailUrl(record), Contract.ImageLimit));
        if (content != record.Content || notes != record.ReleaseNotes || Data.Hash(image) != record.ThumbnailSha256)
            throw new InvalidDataException("Release record differs from its repository-owned content.");
    }
    public async Task<ReleaseRecord[]> Collect(string cache)
    {
        var records = new List<ReleaseRecord>();
        using var packages = new OnlineCatalog(Path.Combine(cache, "catalog"));
        foreach (var mod in Catalog.Mods)
        {
            string repository = OnlineCatalog.Repository(mod.Id);
            for (int page = 1; ; page++)
            {
                if (page > 10) throw new InvalidDataException("Release history exceeds the supported scan bound.");
                var releases = await Api(repository + "/releases?per_page=100&page=" + page);
                foreach (var release in releases.EnumerateArray().Where(Stable))
                {
                    var assets = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == Contract.AssetName).ToArray();
                    if (assets.Length == 0) continue; // Pre-migration release or publisher still running.
                    if (assets.Length != 1) throw new InvalidDataException("Duplicate release metadata assets.");
                    var tag = release.GetProperty("tag_name").GetString()!;
                    Contract.Identity(repository, tag, new string('0', 40));
                    var record = Contract.Validate(Data.Read<ReleaseRecord>(await Read(RecordUrl(repository, tag), Contract.RecordLimit)));
                    if (record.Repository != repository || record.Tag != tag || record.Content.Id != mod.Id)
                        throw new InvalidDataException("Release record identity differs from its approved source.");
                    await VerifySource(record);
                    var item = new CatalogItem(mod.Id, mod.Name, record.Content.Description, repository, [record.Release]);
                    var package = await packages.Download(item, record.Release, Path.Combine(cache, "packages"), default);
                    Contract.Package(record, package.Path); records.Add(record);
                }
                if (releases.GetArrayLength() < 100) break;
            }
        }
        return records.ToArray();
    }
    public void Dispose() => client.Dispose();
}
