using System.IO.Compression;
using System.Reflection;
using System.Text;
using QdsModManager;

internal static class PackageFixtures
{
    internal static void Create(string directory, string oldModule, string newModule, string geometryModule)
    {
        if (Directory.Exists(directory)) throw new IOException("Use a fresh fixture package directory.");
        var harmony = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, Catalog.Harmony));
        var license = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, Catalog.License));
        // Keep production dependency hashes and the package validator unchanged.
        if (Data.Hash(harmony) != Catalog.HarmonyHash || Data.Hash(license) != Catalog.LicenseHash)
            throw new InvalidDataException("Restored Harmony dependency does not match the approved bytes.");
        Directory.CreateDirectory(directory);
        foreach (var (id, modulePath) in new[] { ("schematic-tools", oldModule), ("schematic-tools", newModule), ("automatic-geometry", geometryModule) })
        {
            var mod = Catalog.Mod(id);
            string version = AssemblyName.GetAssemblyName(modulePath).Version!.ToString();
            var files = new Dictionary<string, byte[]> {
                [mod.Module] = File.ReadAllBytes(modulePath), [Catalog.Harmony] = harmony,
                [Catalog.License] = license, [mod.Marker] = Encoding.UTF8.GetBytes(Catalog.HostHash)
            };
            var layout = mod.Layouts.Single(l => l.Schema == 1);
            var manifest = new PackageManifest(1, id, mod.Name, version, "Synthetic lifecycle fixture; never install in Designer.",
                Catalog.HostHash, Catalog.CommonHash, layout.Files.Select(f => new PackageFile(f.Name, Data.Hash(files[f.Name]), files[f.Name].Length, f.Shared)).ToArray());
            string path = Path.Combine(directory, id + "-" + version + ".qdsmod");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                void Entry(string name, byte[] bytes) { var entry = zip.CreateEntry(name); entry.LastWriteTime = new DateTimeOffset(2000,1,1,0,0,0,TimeSpan.Zero); using var stream = entry.Open(); stream.Write(bytes); }
                Entry("manifest.json", Data.Bytes(manifest));
                foreach (var file in manifest.Files) Entry("payload/" + file.Name, files[file.Name]);
            }
            ModPackage.Open(path);
        }
        Console.WriteLine("Three synthetic packages verified by the unchanged production package validator.");
    }
}
