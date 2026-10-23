using System.IO;

namespace QdsModManager.App;

internal sealed partial class MainWindow
{
    private string DescribeGroup(ModProfile next) => string.Join("\n", (inspection?.Profile?.Mods.Select(p => p.Id) ?? []).Union(next.Mods.Select(p => p.Id)).Order().Select(id =>
    {
        var before = inspection?.Profile?.Mods.FirstOrDefault(p => p.Id == id); var after = next.Mods.FirstOrDefault(p => p.Id == id);
        string State(ProfileMod? pin) => pin is null ? "not installed" : pin.Version + (pin.Enabled ? " · enabled" : " · disabled");
        return Catalog.Mod(id).Name + ": " + State(before) + " → " + State(after);
    }));
    private async Task DownloadDependencies(ModPackage selected)
    {
        using var cancellation = new CancellationTokenSource(); network = cancellation; busy = true; Buttons();
        try
        {
            var queue = new Queue<ModPackage>(); queue.Enqueue(selected); var visited = new HashSet<string>();
            while (queue.TryDequeue(out var package))
            {
                if (!visited.Add(package.ArchiveHash)) continue;
                foreach (var requirement in package.Manifest.Dependencies ?? [])
                {
                    var dependency = packages.Where(p => p.Manifest.Id == requirement.Id && Requirements.Matches(requirement, p.Manifest.Version)).OrderByDescending(p => Version.Parse(p.Manifest.Version)).FirstOrDefault();
                    dependency ??= await DownloadRequired(requirement, cancellation.Token);
                    queue.Enqueue(dependency);
                }
            }
        }
        finally { network = null; busy = false; Buttons(); }
    }
    private async Task<ModPackage> DownloadRequired(ModRequirement requirement, CancellationToken cancellation)
    {
        var item = snapshot?.Document.Mods.FirstOrDefault(m => m.Id == requirement.Id);
        var release = item?.Releases.Where(r => CanInstall(r) && Requirements.Matches(requirement, r.Version)).OrderByDescending(r => Version.Parse(r.Version)).FirstOrDefault();
        if (item is null || release is null) throw new InvalidOperationException("Import a compatible package for " + requirement.Id + " before continuing.");
        message.Text = "Verifying " + item.Name + " " + release.Version + "…";
        var package = await online.Download(item, release, Path.Combine(userData, "Packages"), cancellation); packages.Add(package); return package;
    }
    private async Task MigrateSchematicTools()
    {
        if (busy || inspection is null) return;
        var before = inspection; string chosenTarget = target.Text;
        try
        {
            busy = true; using var cancellation = new CancellationTokenSource(); network = cancellation; Buttons();
            foreach (var id in Manager.SchematicToolsReplacementIds)
                if (!packages.Any(p => p.Manifest.Id == id)) await DownloadRequired(new(id,"0.1.0"), cancellation.Token);
            var plan = new Manager(chosenTarget).PrepareSchematicToolsMigration(packages);
            if (before.Stamp != plan.ExpectedStamp) throw new InvalidOperationException("The installation changed. Refresh and preview the migration again.");
            string detail = DescribeGroup(plan.Group!.Profile) + "\n\nReplace the bundle with eight independently managed packages in one undoable operation. The profile keeps its name and unrelated exact versions. Named Signal Colors imports your existing palette; its original file is retained. Historical packages and settings remain available for rollback.";
            busy = false; network = null; Buttons();
            await ExecutePlan(plan, "Split Schematic Tools", detail);
        }
        catch (OperationCanceledException) { message.Text = "Migration canceled. No installation changes were made."; }
        catch (Exception error) { RecordError(error); }
        finally { busy = false; network = null; Buttons(); }
    }
}
