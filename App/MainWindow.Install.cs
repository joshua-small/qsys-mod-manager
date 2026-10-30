using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace QdsModManager.App;

internal sealed partial class MainWindow
{
    private readonly HashSet<string> selectedModIds = new(StringComparer.Ordinal);
    private readonly Button installSelected = Button("Install selected"), clearSelection = Button("Clear selection");
    private readonly TextBlock selectionCount = Muted("Select mods using the checkboxes", new Thickness(0, 0, 12, 0));

    private void RefreshSelection()
    {
        selectionCount.Text = selectedModIds.Count == 0 ? "Select mods using the checkboxes" : selectedModIds.Count + " selected";
        installSelected.Content = selectedModIds.Count == 0 ? "Install selected" : "Install selected (" + selectedModIds.Count + ")";
        installSelected.IsEnabled = !busy && selectedModIds.Count > 0 && inspection is { Pending: null, Suspended: false };
        clearSelection.IsEnabled = !busy && selectedModIds.Count > 0;
    }
    private void SelectMod(CheckBox box)
    {
        if (box.DataContext is not ModRow row || busy) return;
        row.Selected = box.IsChecked is true;
        if (row.Selected) selectedModIds.Add(row.Id); else selectedModIds.Remove(row.Id);
        RefreshSelection();
    }
    private VersionChoice[] InstallOptions()
    {
        var choices = new List<VersionChoice>();
        foreach (var item in snapshot?.Document.Mods ?? [])
            foreach (var release in item.Releases.Where(CanInstall))
                choices.Add(new(release.Version, release.Version, packages.FirstOrDefault(p => p.ArchiveHash == release.Sha256), release, item));
        foreach (var package in packages.Where(p => CanInstall(p) && !choices.Any(c => ChoiceKey(c) == p.ArchiveHash)))
            choices.Add(new(package.Manifest.Version, package.Manifest.Version, package, null, null));
        return choices.ToArray();
    }
    private static string ChoiceKey(VersionChoice choice) => choice.Package?.ArchiveHash ?? choice.Release!.Sha256;
    private static DependencyChoice Policy(VersionChoice choice) => choice.Package is { } package ? DependencyChoice.From(package) : DependencyChoice.From(choice.Item!, choice.Release!);
    private async Task InstallSelected()
    {
        if (busy || selectedModIds.Count == 0) return;
        try
        {
            var available = InstallOptions();
            var requested = selectedModIds.Select(id => available.Where(c => Policy(c).Id == id).OrderByDescending(c => Version.Parse(c.Version)).FirstOrDefault()
                ?? throw new InvalidOperationException("No installable package is available for " + Catalog.Mod(id).Name + ". Refresh the catalog or import its package.")).ToArray();
            await InstallChoices(requested);
        }
        catch (Exception error) { OperationError(error); }
    }
    private async Task InstallChoices(VersionChoice[] requested)
    {
        if (busy || inspection is not { Pending: null, Suspended: false } before) return;
        string chosenTarget = before.Target;
        try
        {
            var options = InstallOptions().Concat(requested).DistinctBy(ChoiceKey).ToDictionary(ChoiceKey);
            var enabled = (before.Profile?.Mods ?? []).Where(p => p.Enabled).Select(pin => packages.FirstOrDefault(p => p.ArchiveHash == pin.Sha256)
                ?? throw new InvalidOperationException("Missing installed package: " + Catalog.Mod(pin.Id).Name + " " + pin.Version)).Select(DependencyChoice.From).ToArray();
            var resolved = Requirements.ResolveChoices(enabled, requested.Select(Policy), options.Values.Select(Policy));
            var requestedIds = requested.Select(c => Policy(c).Id).ToHashSet();
            var dependencies = resolved.Where(c => !requestedIds.Contains(c.Id) && !enabled.Any(p => p.Id == c.Id && p.Key == c.Key)).ToArray();
            if (dependencies.Length > 0 && !Confirm("Install required dependencies?",
                "The selected mods also need:\n\n" + string.Join("\n", dependencies.Select(c => "• " + c.Name + " " + c.Version))
                + "\n\nThese dependencies will be installed or enabled with your selected mods. Cancel leaves your installation unchanged.",
                chosenTarget, "Install all", dialogTitle: "Required dependencies")) return;
            busy = true; using var cancellation = new CancellationTokenSource(); network = cancellation; Buttons();
            foreach (var item in resolved)
            {
                if (packages.Any(p => p.ArchiveHash == item.Key)) continue;
                var choice = options[item.Key];
                if (choice.Item is null || choice.Release is null) throw new InvalidOperationException("Missing package: " + item.Name);
                message.Text = "Downloading " + item.Name + " " + item.Version + "…";
                var package = await online.Download(choice.Item, choice.Release, Path.Combine(userData, "Packages"), cancellation.Token);
                packages.Add(package);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            var selected = requested.Select(c => packages.Single(p => p.ArchiveHash == ChoiceKey(c))).ToArray();
            var plan = new Manager(chosenTarget).PrepareInstall(selected, packages);
            if (plan.ExpectedStamp != before.Stamp) throw new InvalidOperationException("The installation changed while downloading. Refresh and try again.");
            if (!plan.Group!.Profile.Mods.Where(p => p.Enabled).Select(p => p.Sha256).Order().SequenceEqual(resolved.Select(p => p.Key).Order()))
                throw new InvalidOperationException("The package dependencies changed while preparing the installation. Refresh and try again.");
            string caption = selected.Length == 1 ? "Install " + selected[0].Manifest.Name : "Install " + selected.Length + " selected mods";
            busy = false; network = null; Buttons();
            await ExecutePlan(plan, caption, DescribeGroup(plan.Group.Profile) + "\n\nPrevious files are kept for one grouped Undo.");
        }
        catch (OperationCanceledException) { message.Text = "Download canceled. Nothing was installed."; }
        catch (Exception error) { OperationError(error); }
        finally { busy = false; network = null; string feedback = message.Text; Refresh(); message.Text = feedback; Buttons(); }
    }
    private void OperationError(Exception error)
    {
        RecordError(error);
        Confirm("Could not complete change", error.Message, action: "OK", dialogTitle: "Could not complete change", allowCancel: false);
    }
    private bool RequireDesignerClosed(string installation)
    {
        while (new Manager(installation).Inspect(packages).Running)
        {
            if (!Confirm("Close Q-SYS Designer to continue",
                "Save your open designs, then close this Designer installation. Choose Retry to continue. Cancel leaves this operation unapplied and keeps any downloaded packages.\n\nQMM will not close Designer automatically.",
                installation, "Retry", dialogTitle: "Designer is still open")) return false;
        }
        return true;
    }
}
