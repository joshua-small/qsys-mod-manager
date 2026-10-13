using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using QdsModManager;

if (args.Length is not (3 or 4 or 5)) throw new ArgumentException("Usage: pack <mod-id> <payload-dir> <new-package> [approvals.json] [catalog/mod.json]");
if (args.Length >= 4) Catalog.ForTool(Data.Read<ApprovalDocument>(File.ReadAllBytes(args[3])));
var mod = Catalog.Mod(args[0]);
string description = mod.Description;
if (args.Length == 5)
{
    using var source = JsonDocument.Parse(File.ReadAllBytes(args[4]));
    if (source.RootElement.GetProperty("id").GetString() != mod.Id || source.RootElement.GetProperty("name").GetString() != mod.Name) throw new InvalidDataException("Presentation identity differs from approved package.");
    description = source.RootElement.GetProperty("description").GetString() ?? throw new InvalidDataException("Missing author description.");
}
var layout = mod.Layouts.Last(l => l.Schema == 2);
var contents = layout.Files.ToDictionary(f => f.Name, f => File.ReadAllBytes(Path.Combine(args[1], f.Name)));
var files = layout.Files.Select(f => new PackageFile(f.Name, Data.Hash(contents[f.Name]), contents[f.Name].Length, f.Shared)).ToArray();
var manifest = new PackageManifest(2, mod.Id, mod.Name, AssemblyName.GetAssemblyName(Path.Combine(args[1], mod.Module)).Version!.ToString(), description, Catalog.HostHash, Catalog.CommonHash, files);
using (var output = new FileStream(args[2], FileMode.CreateNew))
using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
{
    using (var entry = zip.CreateEntry("manifest.json").Open()) entry.Write(Data.Bytes(manifest));
    foreach (var (name, bytes) in contents) using (var entry = zip.CreateEntry("payload/" + name).Open()) entry.Write(bytes);
}
var verified = ModPackage.Open(args[2]);
Console.WriteLine(verified.Manifest.Id + " " + verified.Manifest.Version + " " + verified.ArchiveHash);
