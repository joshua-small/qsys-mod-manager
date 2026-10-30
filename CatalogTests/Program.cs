using System.Net;
using System.Text;
using System.IO.Compression;
using QdsModManager;
using QdsModManager.CatalogPublishing;

if (args.Length != 2) throw new ArgumentException("Usage: catalog-tests <package-directory> <new-fixture-directory>");
var fixture = Path.GetFullPath(args[1]); if (Directory.Exists(fixture)) throw new IOException("Use a fresh fixture directory.");
Directory.CreateDirectory(Path.Combine(fixture, "catalog/releases"));
var packages = Directory.GetFiles(args[0], "*.qdsmod").Select(ModPackage.Open).ToArray();
var package = packages.Where(p => p.Manifest.Id == "schematic-tools").MaxBy(p => Version.Parse(p.Manifest.Version))!;
var geometry = packages.Where(p => p.Manifest.Id == "automatic-geometry").MaxBy(p => Version.Parse(p.Manifest.Version))!;
var index = Data.Read<CatalogDocument>(File.ReadAllBytes("catalog/index.json"));
var presentation = PresentationDocument.Bundled();
string repository = OnlineCatalog.Repository("schematic-tools"), commit = new('a', 40), tag = "v" + Version.Parse(package.Manifest.Version).ToString(3);
// A synthetic release pin keeps contract tests independent of historical downloads.
// Only this in-memory test index changes; production catalog files remain untouched.
var release = new CatalogRelease(package.Manifest.Version, "10.5", Catalog.HostHash, Catalog.CommonHash,
    "https://github.com/" + repository + "/releases/download/" + tag + "/fixture.qdsmod", package.ArchiveHash, new FileInfo(package.Path).Length);
index = index with { Mods = index.Mods.Select(m => m.Id != package.Manifest.Id ? m : m with
    { Releases = m.Releases.Where(r => r.Version != release.Version).Append(release).ToArray() }).ToArray() };
var content = new ModContent(1, "schematic-tools", "Schematic Tools", "Source-owned description", "joshua-small", "catalog/icon.ico", "https://github.com/" + repository);
var icon = File.ReadAllBytes("App/Assets/qmm.ico");
File.WriteAllBytes(Path.Combine(fixture, "catalog/mod.json"), Data.Bytes(content));
File.WriteAllBytes(Path.Combine(fixture, "catalog/icon.ico"), icon);
File.WriteAllText(Path.Combine(fixture, "catalog/releases", tag + ".md"), "Verbatim author release notes.\n");
var record = Contract.Publish(repository, tag, commit, fixture, package.Path, Path.GetFileName(new Uri(release.Url).AbsolutePath));
var checks = new List<string>();
void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
void Reject(Action action, string label) { try { action(); } catch { checks.Add(label); return; } throw new Exception("Accepted: " + label); }
async Task RejectAsync(Func<Task> action, string label) { try { await action(); } catch { checks.Add(label); return; } throw new Exception("Accepted: " + label); }
Check(record.Release == release && record.ReleaseNotes == "Verbatim author release notes.", "Synthetic package verified; notes normalized by the publishing contract");
Check(Contract.ThumbnailUrl(record).Contains("/" + commit + "/"), "Artwork URL is pinned to content commit");
Check(Catalog.Mod("schematic-tools").Description == index.Mods.Single(m => m.Id == "schematic-tools").Description, "Offline description comes from generated catalog");
Reject(() => Contract.Validate(record with { Repository = "attacker/repo" }), "Unapproved repository rejected");
Reject(() => Contract.Validate(record with { Commit = "main" }), "Moving content reference rejected");
Reject(() => Contract.Validate(record with { Tag = "../main" }), "Path traversal in release tag rejected");
Reject(() => Contract.Validate(record with { Tag = "v99.0.0" }), "Tag and package version mismatch rejected");
Reject(() => Contract.Validate(record with { Content = content with { Id = "unapproved" } }), "Unapproved mod ID rejected");
Reject(() => Contract.Validate(record with { Content = content with { Name = "Renamed identity" } }), "Package identity rename requires approval");
Reject(() => Contract.Validate(record with { Content = content with { Thumbnail = "catalog/../secret.png" } }), "Artwork path traversal rejected");
Reject(() => Contract.Validate(record with { Content = content with { Thumbnail = "https://example.org/a.png" } }), "External artwork source rejected");
Reject(() => Contract.Validate(record with { Content = content with { HelpUrl = "https://github.com/attacker/repo" } }), "External help repository rejected");
Reject(() => Contract.Validate(record with { Content = content with { Author = "Display name" } }), "Non-username author rejected");
Reject(() => Contract.Validate(record with { ReleaseNotes = new string('x', 12001) }), "Oversized notes rejected");
Reject(() => Contract.Validate(record with { Release = release with { HostSha256 = new string('0', 64) } }), "Unsupported Designer binary rejected");
Reject(() => Contract.Validate(record with { Release = release with { CommonSha256 = new string('0', 64) } }), "Unsupported common binary rejected");
Reject(() => Contract.Validate(record with { Release = release with { DesignerVersion = "11" } }), "Unsupported Designer version rejected");
var exactPath = Path.Combine(fixture, "exact-1051.qdsmod");
var exactManifest = package.Manifest with { Schema = 2, HostSha256 = PackageCompatibility.Host1051, CommonSha256 = PackageCompatibility.Common1051,
    Files = package.Manifest.Files.Where(f => f.Name != Catalog.Mod(package.Manifest.Id).LegacyMarker).ToArray() };
using (var zip = ZipFile.Open(exactPath, ZipArchiveMode.Create))
{
    using (var entry = zip.CreateEntry("manifest.json").Open()) entry.Write(Data.Bytes(exactManifest));
    foreach (var file in exactManifest.Files) using (var entry = zip.CreateEntry("payload/" + file.Name).Open()) entry.Write(package.Files[file.Name]);
}
var exactRecord = Contract.Publish(repository, tag, commit, fixture, exactPath, "exact-1051.qdsmod");
Check(exactRecord.Release.DesignerVersion == "10.5.1" && exactRecord.Release.HostSha256 == PackageCompatibility.Host1051, "Publishing derives exact 10.5.1 label from verified package, never historical 10.5");
Contract.Package(exactRecord, exactPath); checks.Add("10.5.1 descriptor and package independently verify together");
Reject(() => Contract.Validate(exactRecord with { Release = exactRecord.Release with { DesignerVersion = "10.5" } }), "Publishing rejects mislabeled 10.5.1 descriptor");
Reject(() => Contract.Validate(exactRecord with { Release = exactRecord.Release with { CommonSha256 = Catalog.CommonHash } }), "Publishing rejects mixed target descriptor");
Reject(() => Contract.Package(exactRecord, package.Path), "10.5.1 descriptor cannot substitute a 10.5.0 archive");
Reject(() => Generator.Merge(index, presentation, new(1, []), [exactRecord]), "Existing version cannot be retargeted to 10.5.1 or replace its immutable archive");
Reject(() => Contract.Validate(record with { Release = release with { Version = Version.Parse(release.Version).ToString(3) } }), "Noncanonical version aliases rejected");
Reject(() => Contract.Validate(record with { Release = release with { Url = release.Url.Replace(repository, "attacker/repo") } }), "Foreign package rejected");
Reject(() => Contract.Package(record with { Release = release with { Sha256 = new string('0', 64) } }, package.Path), "Mismatched package hash rejected");
Reject(() => Contract.Package(record with { Release = release with { Size = release.Size + 1 } }, package.Path), "Mismatched package size rejected");
Reject(() => Contract.Package(record, geometry.Path), "Different valid mod package rejected");
var tampered = Path.Combine(fixture, "tampered.qdsmod"); File.WriteAllBytes(tampered, File.ReadAllBytes(package.Path)[..100]);
Reject(() => Contract.Package(record, tampered), "Damaged archive rejected by existing package verifier");
Reject(() => Contract.Image(new byte[Contract.ImageLimit + 1]), "Oversized artwork rejected");
Reject(() => Contract.Image(Encoding.UTF8.GetBytes("<svg>unapproved format</svg>")), "Unsupported artwork format rejected");
var merged = Generator.Merge(index, presentation, new(1, []), [record]);
Check(merged.Index.Mods.Sum(m => m.Releases.Length) == index.Mods.Sum(m => m.Releases.Length), "Migration preserves all existing version pins");
Check(merged.Presentation.Mods.Single(m => m.Id == content.Id).ReleaseNotes[release.Version] == record.ReleaseNotes, "Generated presentation uses source-owned notes");
Check(merged.Index.Mods.Single(m => m.Id == content.Id).Description == content.Description, "Generated description uses source-owned text");
Check(merged.Sources.Releases.Single().Commit == commit, "Generated provenance records immutable source");
var again = Generator.Merge(merged.Index, merged.Presentation, merged.Sources, [record]);
Check(Data.Bytes(merged.Index).SequenceEqual(Data.Bytes(again.Index)) && Data.Bytes(merged.Presentation).SequenceEqual(Data.Bytes(again.Presentation))
    && Data.Bytes(merged.Sources).SequenceEqual(Data.Bytes(again.Sources)), "Repeated generation is deterministic and idempotent");
Reject(() => Generator.Merge(index, presentation, new(1, []), [record, record]), "Duplicate version records rejected");
Reject(() => Generator.Merge(index, presentation, new(1, []), [record with { Release = release with { Sha256 = new string('0', 64) } }]), "Existing package pin cannot change");
Reject(() => Generator.Merge(merged.Index, merged.Presentation, merged.Sources, [record with { ReleaseNotes = "Rewritten" }]), "Approved notes cannot silently change");
Reject(() => Generator.Merge(merged.Index, merged.Presentation, merged.Sources, [record with { Commit = new string('b', 40) }]), "Approved source cannot silently change");
var future = record with { Tag = "v99.0.0", Release = release with { Version = "99.0.0.0", Url = release.Url.Replace(tag, "v99.0.0") }, Content = content with { Description = "Future release" }, ReleaseNotes = "Future notes" };
var added = Generator.Merge(merged.Index, merged.Presentation, merged.Sources, [future]);
Check(added.Index.Mods.Single(m => m.Id == content.Id).Releases.Length == merged.Index.Mods.Single(m => m.Id == content.Id).Releases.Length + 1, "New version appended without deleting old pins");
var late = Generator.Merge(added.Index, added.Presentation, added.Sources, [record]);
Check(late.Index.Mods.Single(m => m.Id == content.Id).Description == "Future release", "Late older release does not roll current copy back");
Check(late.Presentation.Mods.Single(m => m.Id == content.Id).ReleaseNotes.ContainsKey(release.Version), "Historical notes survive a new release");
var handler = new Handler(uri => new(HttpStatusCode.OK) { Content = new ByteArrayContent(uri.AbsolutePath.EndsWith("mod.json") ? Data.Bytes(content) : uri.AbsolutePath.EndsWith(".md") ? Encoding.UTF8.GetBytes(record.ReleaseNotes) : icon) });
using var github = new GitHub(new HttpClient(handler));
await github.VerifySource(record); checks.Add("Repository source, notes and image hash verified together");
handler.Get = _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Data.Bytes(content with { Description = "Changed" })) };
await RejectAsync(() => github.VerifySource(record), "Source content mismatch rejected");
handler.Get = uri => new(HttpStatusCode.OK) { Content = new ByteArrayContent(uri.AbsolutePath.EndsWith("mod.json") ? Data.Bytes(content) : uri.AbsolutePath.EndsWith(".md") ? Encoding.UTF8.GetBytes("Different notes") : icon) };
await RejectAsync(() => github.VerifySource(record), "Source notes mismatch rejected");
handler.Get = uri => new(HttpStatusCode.OK) { Content = new ByteArrayContent(uri.AbsolutePath.EndsWith("mod.json") ? Data.Bytes(content) : uri.AbsolutePath.EndsWith(".md") ? Encoding.UTF8.GetBytes(record.ReleaseNotes) : icon.Concat(new byte[]{0}).ToArray()) };
await RejectAsync(() => github.VerifySource(record), "Source artwork hash mismatch rejected");
handler.Get = _ => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://example.org/asset") } };
await RejectAsync(() => github.Read(Contract.ThumbnailUrl(record), Contract.ImageLimit), "Untrusted redirect rejected");
handler.Get = _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[100]) };
await RejectAsync(() => github.Read(Contract.ThumbnailUrl(record), 10), "Network response bound enforced");
handler.Get = _ => throw new HttpRequestException("offline");
await RejectAsync(() => github.VerifySource(record), "Offline publishing fails without altering catalog");
var receipt = merged.Sources.Releases.Single();
object[] Listing(string? digest) => [new { draft = false, prerelease = false, tag_name = tag, assets = new[] { new { name = Contract.AssetName, digest } } }];
HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
int dataRequests = 0;
handler.Get = uri =>
{
    if (uri.Host == "api.github.com") return Response(Data.Bytes(uri.AbsolutePath.Contains(repository) ? Listing("sha256:" + receipt.RecordSha256) : Array.Empty<object>()));
    dataRequests++; throw new Exception("Unchanged history should not download payloads.");
};
Check((await github.Collect(Path.Combine(fixture, "repeat-cache"), merged.Sources)).Length == 0 && dataRequests == 0, "Matching GitHub descriptor digest reuses signed history without asset downloads");
handler.Get = uri =>
{
    if (uri.Host == "api.github.com") return Response(Data.Bytes(uri.AbsolutePath.Contains(repository) ? Listing(null) : Array.Empty<object>()));
    dataRequests++; return Response(Data.Bytes(record));
};
Check((await github.Collect(Path.Combine(fixture, "legacy-digest-cache"), merged.Sources)).Length == 0 && dataRequests == 1, "Assets without a digest verify the descriptor without redownloading the package");
handler.Get = uri => uri.Host == "api.github.com"
    ? Response(Data.Bytes(uri.AbsolutePath.Contains(repository) ? Listing(null) : Array.Empty<object>()))
    : Response(Data.Bytes(record with { ReleaseNotes = "Replaced after approval" }));
await RejectAsync(() => github.Collect(Path.Combine(fixture, "changed-history"), merged.Sources), "Changed historical descriptor blocks publication");
handler.Get = _ => Response(Data.Bytes(Array.Empty<object>()));
await RejectAsync(() => github.Collect(Path.Combine(fixture, "missing-history"), merged.Sources), "Missing approved descriptor blocks publication without erasing pins");
handler.Get = uri =>
{
    if (uri.Host == "api.github.com") return Response(Data.Bytes(uri.AbsolutePath.Contains(repository) ? Listing(null) : Array.Empty<object>()));
    if (uri.AbsolutePath.EndsWith(Contract.AssetName)) return Response(Data.Bytes(record));
    if (uri.AbsolutePath.EndsWith(".qdsmod")) return Response(File.ReadAllBytes(package.Path));
    if (uri.AbsolutePath.EndsWith("mod.json")) return Response(Data.Bytes(content));
    if (uri.AbsolutePath.EndsWith(".md")) return Response(Encoding.UTF8.GetBytes(record.ReleaseNotes));
    return Response(icon);
};
Check((await github.Collect(Path.Combine(fixture, "new-release"), new(1, []))).Single() == record, "A new release still verifies source, artwork and the full package");
Check(await github.Approved(receipt, Path.Combine(fixture, "approved-release")) == record, "Approved packages use the supplied transport and keep it alive after Collect");
File.WriteAllBytes(Path.Combine(fixture, "results.json"), Data.Bytes(checks));
Console.WriteLine(checks.Count + " catalog publishing checks passed.");

sealed class Handler(Func<Uri, HttpResponseMessage> get) : HttpMessageHandler
{
    public Func<Uri, HttpResponseMessage> Get = get;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(Get(request.RequestUri!));
}
