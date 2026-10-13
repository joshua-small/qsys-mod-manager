using System.Net;
using QdsModManager;

internal static class CatalogChecks
{
    internal static async Task Run(ModPackage package, string output, List<string> checks)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        async Task Reject(Func<Task> action, string label) { try { await action(); } catch { checks.Add(label); return; } throw new Exception("Accepted: " + label); }
        string root = Path.Combine(output, "catalog"); Directory.CreateDirectory(root);
        var release = new CatalogRelease(package.Manifest.Version, "10.5", Catalog.HostHash, Catalog.CommonHash,
            "https://github.com/joshua-small/qsys-schematic-tools/releases/download/v0.15.0/schematic-tools-0.15.0.qdsmod", package.ArchiveHash, new FileInfo(package.Path).Length);
        var item = new CatalogItem("schematic-tools", "Schematic Tools", "Packing, icons and snapshots", "joshua-small/qsys-schematic-tools", new[] { release });
        var document = new CatalogDocument(1, new[] { item }); var bytes = File.ReadAllBytes(package.Path);
        var signed = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "catalog-signed.json"));
        var handler = new Handler(uri => Response(uri.AbsoluteUri == OnlineCatalog.IndexUrl ? signed : bytes));
        using var online = new OnlineCatalog(root, new HttpClient(handler));
        var fetched = await online.Fetch(default); Check(!fetched.Offline && fetched.Document.Mods.Length == 2, "Signed catalog fetched and validated");
        var presentation = PresentationDocument.Bundled(); handler.Get = _ => Response(Data.Bytes(presentation));
        Check((await online.FetchPresentation(default))?.Mods.Length == 2, "Presentation is part of the verified catalog snapshot");
        handler.Get = _ => throw new HttpRequestException("offline");
        Check((await online.FetchPresentation(default))?.Mods.Length == 2, "Cached release notes work offline");
        await Reject(() => Task.FromResult(PresentationDocument.Validate(presentation with { Mods = [presentation.Mods[0] with { Thumbnail = "https://example.com/image.png" }] })), "Untrusted thumbnail host rejected");
        handler.Get = uri => Response(uri.AbsoluteUri == OnlineCatalog.IndexUrl ? signed : bytes);
        Check(OnlineCatalog.Compatible(release, Catalog.HostHash, Catalog.CommonHash), "Matching host is compatible");
        Check(!OnlineCatalog.Compatible(release, new string('0', 64), Catalog.CommonHash), "Different host is incompatible");
        Check(!OnlineCatalog.Compatible(release, Catalog.HostHash, null), "Missing dependency is incompatible");
        Check(OnlineCatalog.Matches(item, "SNAP packing") && !OnlineCatalog.Matches(item, "speaker"), "Search matches all words in name and description");
        var downloaded = await online.Download(item, release, Path.Combine(root, "packages"), default);
        Check(downloaded.ArchiveHash == package.ArchiveHash, "Downloaded package verified against catalog and manifest");
        handler.Get = _ => throw new HttpRequestException("offline");
        Check((await online.Fetch(default)).Offline, "Offline catalog uses validated cached data");
        Check((await online.Download(item, release, Path.Combine(root, "packages"), default)).ArchiveHash == package.ArchiveHash, "Previously downloaded package works offline");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Reject(() => online.Fetch(canceled.Token), "Canceled refresh does not silently use cache");
        File.WriteAllText(Path.Combine(root, "packages", package.ArchiveHash + ".qdsmod"), "external file");
        await Reject(() => online.Download(item, release, Path.Combine(root, "packages"), default), "Changed cached package is preserved and rejected");
        Check(File.ReadAllText(Path.Combine(root, "packages", package.ArchiveHash + ".qdsmod")) == "external file", "Changed download cache not overwritten");
        async Task Bad(CatalogRelease bad, string label) => await Reject(() => Task.FromResult(OnlineCatalog.Validate(new(1, new[] { item with { Releases = new[] { bad } } }))), label);
        await Bad(release with { Url = "https://example.org/mod.qdsmod" }, "Foreign package host rejected");
        await Bad(release with { Url = release.Url.Replace("https:", "http:") }, "Unencrypted package address rejected");
        await Bad(release with { Url = release.Url.Replace("qsys-schematic-tools", "unknown-repository") }, "Unexpected release repository rejected");
        await Bad(release with { Sha256 = "wrong" }, "Malformed catalog checksum rejected");
        await Bad(release with { Size = 30 * 1024 * 1024 }, "Oversized catalog package rejected");
        await Reject(() => Task.FromResult(OnlineCatalog.Validate(new(1, new[] { item, item }))), "Duplicate catalog ID rejected");
        await Reject(() => Task.FromResult(OnlineCatalog.Validate(new(2, new[] { item }))), "Unknown catalog schema rejected");
        handler.Get = _ => Response(new byte[bytes.Length]);
        await Reject(() => online.Download(item, release, Path.Combine(root, "tampered"), default), "Tampered download rejected before installation");
        Check(!Directory.GetFiles(Path.Combine(root, "tampered")).Any(), "Rejected download leaves no package");
        handler.Get = _ => Response(new byte[bytes.Length + 1]);
        await Reject(() => online.Download(item, release, Path.Combine(root, "large"), default), "Oversized response rejected");
        handler.Get = _ => { var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://example.org/download"); return response; };
        await Reject(() => online.Download(item, release, Path.Combine(root, "redirect"), default), "Foreign release redirect rejected");
        handler.Get = uri => { if (uri.Host == "release-assets.githubusercontent.com") return Response(bytes); var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://release-assets.githubusercontent.com/fixture"); return response; };
        Check((await online.Download(item, release, Path.Combine(root, "github-redirect"), default)).ArchiveHash == package.ArchiveHash, "GitHub release asset redirect supported");
        handler.Get = _ => Response(Data.Bytes(new { schema = 1, mods = new[] { item with { Name = "wrong" } } }));
        Check((await online.Fetch(default)).Offline, "Invalid online catalog preserves last validated cache");
        string settings = Path.Combine(root, "settings.json");
        Check(Preferences.Load(settings).Theme == Appearance.System, "First launch follows system");
        new Preferences(Appearance.Dark).Save(settings); Check(Preferences.Load(settings).IsDark(false), "Dark preference persists across reload");
        new Preferences(Appearance.Light).Save(settings); Check(!Preferences.Load(settings).IsDark(true), "Light preference overrides dark system");
        new Preferences().Save(settings); Check(Preferences.Load(settings).IsDark(true) && !Preferences.Load(settings).IsDark(false), "Follow System resolves both system modes");
        File.WriteAllText(settings, "{broken"); Check(Preferences.Load(settings).Theme == Appearance.System, "Damaged preferences fall back to system");
        await Reject(() => Task.Run(() => new Preferences((Appearance)999).Save(settings)), "Invalid preference cannot be saved");
    }
    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private sealed class Handler(Func<Uri, HttpResponseMessage> get) : HttpMessageHandler
    {
        internal Func<Uri, HttpResponseMessage> Get = get;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Get(request.RequestUri!)); }
    }
}
