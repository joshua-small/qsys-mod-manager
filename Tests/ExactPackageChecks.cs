using System.IO.Compression;
using System.Security.Cryptography;
using QdsModManager;

internal static class ExactPackageChecks
{
    internal static ModPackage Package(string path, ModPackage source, string host = PackageCompatibility.Host1051, string common = PackageCompatibility.Common1051)
    {
        var manifest = source.Manifest with { Schema = 2, HostSha256 = host, CommonSha256 = common,
            Files = source.Manifest.Files.Where(f => f.Name != Catalog.Mod(source.Manifest.Id).LegacyMarker).ToArray() };
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var entry = zip.CreateEntry("manifest.json").Open()) entry.Write(Data.Bytes(manifest));
            foreach (var file in manifest.Files) using (var entry = zip.CreateEntry("payload/" + file.Name).Open()) entry.Write(source.Files[file.Name]);
        }
        return ModPackage.Open(path);
    }
    internal static void Run(string output, ModPackage source, byte[] host, byte[] common, List<string> checks)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); }
        void Reject(Action action, string label) { try { action(); } catch { checks.Add(label); return; } throw new Exception("Accepted: " + label); }
        var exact = Package(Path.Combine(output, "exact-1051.qdsmod"), source);
        Check(exact.Manifest.HostSha256 == PackageCompatibility.Host1051 && exact.Manifest.CommonSha256 == PackageCompatibility.Common1051, "Exact 10.5.1 package retains both declared hashes");
        Check(PackageCompatibility.Exact(PackageCompatibility.Host1051, PackageCompatibility.Common1051, PackageCompatibility.Host1051, PackageCompatibility.Common1051), "10.5.1 package matches its exact target");
        foreach (var (h, c) in new[] { (Catalog.HostHash, Catalog.CommonHash), (Catalog.HostHash, PackageCompatibility.Common1051), (PackageCompatibility.Host1051, Catalog.CommonHash), (new string('f', 64), new string('e', 64)) })
            Check(!PackageCompatibility.CanUse(PackageCompatibility.Host1051, PackageCompatibility.Common1051, h, c), "10.5.1-only package rejects other or mixed target " + h[..8] + "/" + c[..8]);
        Check(PackageCompatibility.CanUse(Catalog.HostHash, Catalog.CommonHash, PackageCompatibility.Host1051, PackageCompatibility.Common1051)
            && !PackageCompatibility.Exact(Catalog.HostHash, Catalog.CommonHash, PackageCompatibility.Host1051, PackageCompatibility.Common1051), "Legacy opt-in remains installable but does not become exact-compatible on 10.5.1");
        Reject(() => Package(Path.Combine(output, "mixed-package.qdsmod"), source, PackageCompatibility.Host1051, Catalog.CommonHash), "Mixed package host/common pair rejected");
        Reject(() => Package(Path.Combine(output, "unknown-package.qdsmod"), source, new string('f', 64), PackageCompatibility.Common1051), "Unknown package host rejected");
        var proof = Data.Read<SignedCatalog>(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "catalog-signed.json")));
        var feed = CatalogTrust.Verify(proof);
        var item = feed.Index.Mods.Single(m => m.Id == exact.Manifest.Id);
        var release = item.Releases[0] with { DesignerVersion = "10.5.1", HostSha256 = PackageCompatibility.Host1051, CommonSha256 = PackageCompatibility.Common1051 };
        CatalogDocument Index(CatalogRelease r) => feed.Index with { Mods = feed.Index.Mods.Select(m => m.Id == item.Id ? m with { Releases = [r] } : m).ToArray() };
        Check(OnlineCatalog.Validate(Index(release)).Mods.Length == feed.Index.Mods.Length, "Catalog admits explicit 10.5.1 release while preserving old entries");
        Reject(() => OnlineCatalog.Validate(Index(release with { DesignerVersion = "10.5" })), "Catalog rejects 10.5.1 package mislabeled as historical 10.5");
        Reject(() => OnlineCatalog.Validate(Index(release with { CommonSha256 = Catalog.CommonHash })), "Catalog rejects mixed known binaries");
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            CatalogFeed Verify(CatalogDocument index) { var bytes = Data.Bytes(feed with { Index = index }); return CatalogTrust.Verify(new(1, Convert.ToBase64String(bytes), Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256))), key); }
            Check(Verify(Index(release)).Index.Mods.Length > 0, "Signed feed validation admits recognized exact target");
            Reject(() => Verify(Index(release with { HostSha256 = new string('f', 64) })), "Even a valid signature cannot authorize unknown package target");
        }
        string root = Path.Combine(output, "exact-package-other-host"); Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "Q-Sys Designer.dll"), host); File.WriteAllBytes(Path.Combine(root, "ModuleCommon.dll"), common);
        var engine = new Manager(root, Data.Hash(host), Data.Hash(common), _ => false);
        Reject(() => engine.Prepare("Install", exact.Manifest.Id, exact.Path), "Single install cannot enable exact package on another host");
        Reject(() => engine.PrepareInstall(exact, [source, exact]), "Bulk install cannot select exact package on another host");
        var profile = ModProfile.Create("Exact disabled pin", [new(exact.Manifest.Id, exact.Manifest.Version, exact.ArchiveHash, false)]);
        var plan = engine.PrepareProfile(profile, [exact]);
        Check(plan.Group!.Profile.DesignerVersion == "10.5.1" && plan.Group.Profile.HostSha256 == PackageCompatibility.Host1051, "Prepared profile derives exact target from its pinned packages");
        Check(engine.Apply(plan).Success && !File.Exists(Path.Combine(root, Catalog.Mod(exact.Manifest.Id).Module)), "Disabled exact package can be retained without activating on another build");
        Reject(() => engine.Prepare("Enable", exact.Manifest.Id), "Enable cannot bypass exact package target");
        var enabled = plan.Group.Profile with { Mods = plan.Group.Profile.Mods.Select(p => p with { Enabled = true }).ToArray() };
        Reject(() => engine.PrepareProfile(enabled, [exact]), "Profile activation cannot bypass exact package target");
        string export = Path.Combine(output, "exact-profile.qdsprofile"); File.WriteAllBytes(export, Data.Bytes(plan.Group.Profile));
        Check(ModProfile.Import(export).HostSha256 == PackageCompatibility.Host1051, "Profile import/export preserves exact 10.5.1 metadata");
        Check(engine.Apply(engine.PrepareProfile(enabled, [exact], true, "Suspend")).Success, "Suspended profile stores enabled intent without writing exact-target module");
        Reject(() => engine.PrepareProfile(enabled, [exact], false, "Resume"), "Resume cannot bypass exact package target");
        Check(engine.Apply(engine.Prepare("Undo")).Success && !engine.Inspect([exact]).Suspended, "Undo restores earlier disabled snapshot on another target");
        Check(engine.Apply(engine.Prepare("Remove", exact.Manifest.Id)).Success && engine.Inspect([]).Profile!.DesignerVersion == "10.5", "Removing last exact pin restores historical profile metadata");
        Check(!Directory.GetFiles(root, "Designer.Modules.*.dll").Any(), "All rejected activation paths left target modules absent");
    }
}
