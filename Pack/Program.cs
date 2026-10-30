using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using QdsModManager;

if (args.Length is < 3 or > 6) throw new ArgumentException("Usage: pack <mod-id> <payload-dir> <new-package> [approvals.json] [catalog/mod.json] [10.5.0|10.5.1]");
if (args.Length >= 4) Catalog.ForTool(Data.Read<ApprovalDocument>(File.ReadAllBytes(args[3])));
var mod = Catalog.Mod(args[0]);
string description = mod.Description;
if (args.Length >= 5)
{
    using var source = JsonDocument.Parse(File.ReadAllBytes(args[4]));
    if (source.RootElement.GetProperty("id").GetString() != mod.Id || source.RootElement.GetProperty("name").GetString() != mod.Name) throw new InvalidDataException("Presentation identity differs from approved package.");
    description = source.RootElement.GetProperty("description").GetString() ?? throw new InvalidDataException("Missing author description.");
}
var version = AssemblyName.GetAssemblyName(Path.Combine(args[1], mod.Module)).Version!.ToString();
var layout = mod.Layouts.Last(l => l.Schema >= 2 && (l.MinimumVersion is null || Requirements.Matches(new(mod.Id,l.MinimumVersion,l.BeforeVersion),version)));
var contents = layout.Files.ToDictionary(f => f.Name, f => File.ReadAllBytes(Path.Combine(args[1], f.Name)));
var files = layout.Files.Select(f => new PackageFile(f.Name, Data.Hash(contents[f.Name]), contents[f.Name].Length, f.Shared)).ToArray();
string target = args.Length == 6 ? args[5] : "10.5.0";
if (target is not ("10.5.0" or "10.5.1")) throw new InvalidDataException("Choose a recognized exact Designer package target.");
var manifest = new PackageManifest(layout.Schema, mod.Id, mod.Name, version, description,
    target == "10.5.0" ? Catalog.HostHash : PackageCompatibility.Host1051,
    target == "10.5.0" ? Catalog.CommonHash : PackageCompatibility.Common1051, files, layout.Dependencies, layout.Conflicts);
using (var output = new FileStream(args[2], FileMode.CreateNew))
using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
{
    using (var entry = zip.CreateEntry("manifest.json").Open()) entry.Write(Data.Bytes(manifest));
    foreach (var (name, bytes) in contents) using (var entry = zip.CreateEntry("payload/" + name).Open()) entry.Write(bytes);
}
var verified = ModPackage.Open(args[2]);
Console.WriteLine(verified.Manifest.Id + " " + verified.Manifest.Version + " " + verified.ArchiveHash);
