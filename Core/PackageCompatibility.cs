namespace QdsModManager;

/// <summary>Recognized package targets; recognition is not blanket runtime qualification.</summary>
public static class PackageCompatibility
{
    public const string Host1051 = "05215790f1a08bb6bdb9db83a4b06ea631543f44f8cc6da04dc2b72864361fe9";
    public const string Common1051 = "2413609b3c33a30d83506de1473aeea7057da7486133ec38517394d32d028ff1";
    public static bool Legacy(string? host, string? common) => host == Catalog.HostHash && common == Catalog.CommonHash;
    public static string? Version(string? host, string? common) => Legacy(host, common) ? "10.5.0"
        : host == Host1051 && common == Common1051 ? "10.5.1" : null;
    public static bool LabelMatches(string label, string host, string common) => Version(host, common) is { } version
        && (label == version || (label == "10.5" && version == "10.5.0"));
    public static bool Exact(string packageHost, string packageCommon, string? targetHost, string? targetCommon) =>
        Version(packageHost, packageCommon) is not null && packageHost == targetHost && packageCommon == targetCommon;
    // Preserve the existing explicit untested-build route only for historical
    // 10.5.0 packages. New exact-target packages cannot opt out of their target.
    public static bool CanUse(string packageHost, string packageCommon, string? targetHost, string? targetCommon) =>
        targetHost is not null && targetCommon is not null && (Legacy(packageHost, packageCommon) || Exact(packageHost, packageCommon, targetHost, targetCommon));
    public static void Require(PackageManifest manifest, DesignerBuild build)
    {
        if (!CanUse(manifest.HostSha256, manifest.CommonSha256, build.HostSha256, build.CommonSha256))
            throw new InvalidOperationException(manifest.Name + " " + manifest.Version + " requires the exact Designer "
                + Version(manifest.HostSha256, manifest.CommonSha256) + " build. Select that installation before enabling this package.");
    }
}
