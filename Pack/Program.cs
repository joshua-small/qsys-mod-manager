using System.IO.Compression;
using System.Reflection;
using QdsModManager;
if (args.Length != 3) throw new ArgumentException("Usage: pack <schematic-tools|automatic-geometry> <payload-directory> <new-output.qdsmod>");
var mod = Catalog.Mod(args[0]);
var contents = new[] { mod.Module, mod.Marker, Catalog.Harmony, Catalog.License }.ToDictionary(n => n, n => File.ReadAllBytes(Path.Combine(args[1], n)));
var files = contents.Select(p => new PackageFile(p.Key, Data.Hash(p.Value), p.Value.Length, p.Key == Catalog.Harmony || p.Key == Catalog.License)).ToArray();
var manifest = new PackageManifest(1, mod.Id, mod.Name, AssemblyName.GetAssemblyName(Path.Combine(args[1], mod.Module)).Version!.ToString(), mod.Description, Catalog.HostHash, Catalog.CommonHash, files);
using (var output = new FileStream(args[2], FileMode.CreateNew))
using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
{
    using (var stream = zip.CreateEntry("manifest.json").Open()) stream.Write(Data.Bytes(manifest));
    foreach (var (name, bytes) in contents) using (var stream = zip.CreateEntry("payload/" + name, CompressionLevel.Optimal).Open()) stream.Write(bytes);
}
var package = ModPackage.Open(args[2]); Console.WriteLine($"{package.Manifest.Name} {package.Manifest.Version}: {package.ArchiveHash}");
