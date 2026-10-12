using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QdsModManager;

public sealed record ModDefinition(string Id, string Name, string Module, string Marker, string Description);
public static class Catalog
{
    public const string HostHash = "0e7150f8dc02e0159e019188780fb003ce025f5ec18c491609cf337d849bda60";
    public const string CommonHash = "a6d1addda06bdcb1ad32b57c7480c947210c3d3ad287f5818c2ea64068bf5aee";
    public const string HarmonyHash = "fd77b88724f4104440df0cf979a851d35eec75ea3a7e86297d04abe47c71aff6";
    public const string LicenseHash = "407ddd98200c9f17f49942b5a7791e288a2bd553b59bf2f462db87dbb21c50bc";
    public const string Harmony = "0Harmony.dll", License = "AutomaticGeometry.Harmony.LICENSE.txt";
    public static readonly ModDefinition[] Mods =
    {
        new("schematic-tools", "Schematic Tools", "Designer.Modules.SelectionProbe.dll", ".schematic-tools-enabled",
            "Packing and shortcuts, named-signal colors, aligned color palette, icon search, editor shortcuts, and snapshot tools."),
        new("automatic-geometry", "Automatic Geometry", "Designer.Modules.AutomaticGeometry.dll", ".automatic-geometry-enabled",
            "Automatic component widths for control-pin labels, plus 10-unit Container and Channel Group connectors with centered labels and pins.")
    };
    public static ModDefinition Mod(string id) => Mods.SingleOrDefault(m => m.Id == id) ?? throw new InvalidDataException("This package is not one of the supported mod bundles.");
    public static string[] FileNames => Mods.SelectMany(m => new[] { m.Module, m.Marker }).Concat(new[] { Harmony, License }).ToArray();
    public static void FileName(string name) { if (!FileNames.Contains(name, StringComparer.Ordinal)) throw new InvalidDataException("Unsupported package file: " + name); }
}

public static class Data
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    public static void CheckHash(string value) { if (value.Length != 64 || value.Any(c => !char.IsAsciiHexDigit(c)) || value != value.ToLowerInvariant()) throw new InvalidDataException("Invalid content hash."); }
    public static T Read<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("Empty JSON document.");
    public static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
}

public sealed record PackageFile(string Name, string Sha256, long Length, bool Shared);
public sealed record PackageManifest(int Schema, string Id, string Name, string Version, string Description, string HostSha256, string CommonSha256, PackageFile[] Files);

/// <summary>Data-only archive. No install scripts, path extraction, or assembly execution.</summary>
public sealed class ModPackage
{
    public string Path { get; }
    public string ArchiveHash { get; }
    public PackageManifest Manifest { get; }
    public IReadOnlyDictionary<string, byte[]> Files { get; }
    private ModPackage(string path, string hash, PackageManifest manifest, Dictionary<string, byte[]> files)
    { Path = path; ArchiveHash = hash; Manifest = manifest; Files = files; }

    public static ModPackage Open(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 24 * 1024 * 1024) throw new InvalidDataException("Missing or oversized mod package.");
        byte[] archiveBytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(archiveBytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count != 5 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 5)
            throw new InvalidDataException("A package must contain exactly one manifest and four distinct payload files.");
        var manifest = Data.Read<PackageManifest>(Read(zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Missing manifest."), 65536));
        var mod = Catalog.Mod(manifest.Id);
        if (manifest.Schema != 1 || manifest.Name != mod.Name || !Version.TryParse(manifest.Version, out var version) || version.Build < 0
            || manifest.HostSha256 != Catalog.HostHash || manifest.CommonSha256 != Catalog.CommonHash || manifest.Description.Length > 4000)
            throw new InvalidDataException("Unsupported package identity, version or Designer compatibility.");
        var expected = new[] { mod.Module, mod.Marker, Catalog.Harmony, Catalog.License };
        if (manifest.Files.Length != 4 || !manifest.Files.Select(f => f.Name).Order().SequenceEqual(expected.Order())) throw new InvalidDataException("Unexpected mod file set.");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            Catalog.FileName(file.Name); Data.CheckHash(file.Sha256);
            if (file.Shared != (file.Name == Catalog.Harmony || file.Name == Catalog.License)) throw new InvalidDataException("Incorrect shared dependency declaration.");
            byte[] contents = Read(zip.GetEntry("payload/" + file.Name) ?? throw new InvalidDataException("Missing payload."), 8 * 1024 * 1024);
            if (contents.LongLength != file.Length || Data.Hash(contents) != file.Sha256) throw new InvalidDataException("Package content does not match its manifest: " + file.Name);
            files.Add(file.Name, contents);
        }
        if (files.Values.Sum(b => b.Length) > 16 * 1024 * 1024 || Data.Hash(files[Catalog.Harmony]) != Catalog.HarmonyHash || Data.Hash(files[Catalog.License]) != Catalog.LicenseHash)
            throw new InvalidDataException("Package dependency or size does not match this manager's supported build.");
        if (Encoding.UTF8.GetString(files[mod.Marker]).Trim() != Catalog.HostHash) throw new InvalidDataException("Invalid enable marker.");
        using var pe = new PEReader(new MemoryStream(files[mod.Module]));
        var metadata = pe.GetMetadataReader(); var assembly = metadata.GetAssemblyDefinition();
        if (metadata.GetString(assembly.Name) != System.IO.Path.GetFileNameWithoutExtension(mod.Module) || assembly.Version != Normalize(version))
            throw new InvalidDataException("The module name/version differs from the manifest.");
        return new(System.IO.Path.GetFullPath(path), Data.Hash(archiveBytes), manifest, files);
    }
    private static Version Normalize(Version value) => new(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
    private static byte[] Read(ZipArchiveEntry entry, int limit)
    {
        if (entry.Length > limit) throw new InvalidDataException("Oversized archive entry.");
        using var input = entry.Open(); using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = input.Read(buffer)) > 0) { if (output.Length + count > limit) throw new InvalidDataException("Archive expansion exceeds limit."); output.Write(buffer, 0, count); }
        return output.ToArray();
    }
}
