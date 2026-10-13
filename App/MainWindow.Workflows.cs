using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace QdsModManager.App;

internal sealed partial class MainWindow
{
    private ProfileLibrary? profileLibrary;
    private PresentationDocument? presentation = PresentationDocument.Bundled();
    private readonly List<string> errors = new();
    private readonly Dictionary<string, System.Windows.Media.ImageSource> thumbnails = new();
    private async Task LoadThumbnails(CancellationToken cancellation)
    {
        foreach (var mod in presentation?.Mods ?? [])
        {
            try
            {
                var bytes = await online.Thumbnail(mod, cancellation); if (bytes is null) continue;
                using var stream = new MemoryStream(bytes); var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0]; if (frame.PixelWidth > 2048 || frame.PixelHeight > 2048) continue; frame.Freeze(); thumbnails[mod.Id] = frame;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { errors.Add("Thumbnail: " + error.GetBaseException().Message); }
        }
    }
    private void MigrateKnownInstallations()
    {
        try
        {
            profileLibrary ??= new ProfileLibrary(userData);
            foreach (string path in Manager.Discover())
            {
                try { var engine = new Manager(path); profileLibrary.Reconcile(engine.Inspect([])); }
                catch (Exception error) { errors.Add("Profile migration for " + path + ": " + error.GetBaseException().Message); }
            }
        }
        catch (Exception error) { RecordError(error); }
    }
    private void RecordError(Exception error) { string text = error.GetBaseException().Message; errors.Add(DateTimeOffset.Now.ToString("u") + " " + text); message.Text = text; }
    private void RefreshProfiles()
    {
        profileLibrary ??= new ProfileLibrary(userData);
        if (inspection is not null) profileLibrary.Reconcile(inspection);
        string? selected = (profiles.SelectedItem as ModProfile)?.Id;
        profiles.ItemsSource = profileLibrary.Store.Profiles;
        profiles.SelectedItem = profileLibrary.Store.Profiles.FirstOrDefault(p => p.Id == inspection?.Profile?.Id) ?? profileLibrary.Store.Profiles.FirstOrDefault(p => p.Id == selected);
    }
    private void RefreshSidebar()
    {
        bool ready = !busy && inspection is not null && inspection.Pending is null;
        compatibility.Visibility = inspection is not null && !inspection.Running && inspection.Pending is null ? Visibility.Collapsed : Visibility.Visible;
        progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        recoverNow.Visibility = inspection?.Pending is not null ? Visibility.Visible : Visibility.Collapsed; recoverNow.IsEnabled = !busy;
        suspend.Content = inspection?.Suspended is true ? "Mods off · Turn on" : "Mods on · Turn off"; suspend.IsEnabled = ready;
        SetButtonIcon(suspend, inspection?.Suspended is true ? "Prohibited" : "CheckmarkCircle");
        launch.IsEnabled = ready && File.Exists(Path.Combine(target.Text, "Q-Sys Designer.exe"));
        Explain(launch, inspection?.Pending is not null ? "Recover the interrupted operation before launching Designer." : "Open the selected Designer installation");
        profiles.IsEnabled = !busy; applyProfile.IsEnabled = ready && profiles.SelectedItem is ModProfile;
        foreach (var button in profileButtons) button.IsEnabled = !busy;
        foreach (var item in profileCommands) item.IsEnabled = !busy && (item.Tag as string == "Import" || profiles.SelectedItem is ModProfile);
        foreach (var button in utilityButtons) button.IsEnabled = !busy;
        browse.IsEnabled = refresh.IsEnabled = import.IsEnabled = !busy;
        var available = AvailableUpdates(); updates.Content = available.Length == 0 ? "No mod updates" : available.Length + (available.Length == 1 ? " mod update available" : " mod updates available"); updates.IsEnabled = ready && available.Length > 0;
        updates.Tag = available.Length > 0 ? "Primary" : null;
        bool differs = profiles.SelectedItem is ModProfile selected && (inspection?.Profile is not { } current || selected.Id != current.Id || !selected.Mods.SequenceEqual(current.Mods));
        applyProfile.Visibility = differs ? Visibility.Visible : Visibility.Collapsed;
        profileState.Text = differs ? "Selection not applied" : ""; profileState.Visibility = differs ? Visibility.Visible : Visibility.Collapsed;
        unmanagedState.Text = inspection?.Unmanaged is { Length: > 0 } unmanaged ? unmanaged.Length + (unmanaged.Length == 1 ? " unmanaged module remains" : " unmanaged modules remain") : "";
        unmanagedState.Visibility = string.IsNullOrEmpty(unmanagedState.Text) ? Visibility.Collapsed : Visibility.Visible;
        unmanagedState.ToolTip = "Turning mods off leaves these files in place:\n" + string.Join("\n", inspection?.Unmanaged ?? []);
        catalogStatus.Visibility = catalogStatus.Text.StartsWith("Catalog up to date", StringComparison.Ordinal) ? Visibility.Collapsed : Visibility.Visible;
        undo.Tag = inspection?.Pending is not null ? "Primary" : null;
        RefreshNotice();
    }
    private void EditProfile(string action)
    {
        if (busy) return;
        try
        {
            profileLibrary ??= new ProfileLibrary(userData); var selected = profiles.SelectedItem as ModProfile;
            switch (action)
            {
                case "Add": case "Duplicate":
                    string? name = PromptName(action == "Add" ? "New blank profile" : "Duplicate profile", action == "Add" ? "New profile" : (selected?.Name ?? "Profile") + " copy"); if (name is null) return;
                    var created = action == "Duplicate" && selected is not null ? selected with { Id = Guid.NewGuid().ToString("N"), Name = name } : ModProfile.Create(name); profileLibrary.Save(created); RefreshProfiles(); profiles.SelectedItem = profileLibrary.Store.Profiles.Single(p => p.Id == created.Id); break;
                case "Rename":
                    if (selected is null) return; string? renamed = PromptName("Rename profile", selected.Name); if (renamed is null) return; profileLibrary.Save(selected with { Name = renamed }); RefreshProfiles(); break;
                case "Delete":
                    if (selected is null) return; if (Preview("Delete “" + selected.Name + "”?", "Remove this saved profile. Installed files will not change.", "Profile library", "Delete profile")) { profileLibrary.Delete(selected.Id); RefreshProfiles(); } break;
                case "Import":
                    var open = new OpenFileDialog { Filter = "Mod profiles (*.qdsprofile;*.json)|*.qdsprofile;*.json", Title = "Import profile metadata" }; if (open.ShowDialog(this) is not true) return;
                    var imported = ModProfile.Import(open.FileName); profileLibrary.Save(imported); RefreshProfiles(); profiles.SelectedItem = profileLibrary.Store.Profiles.Single(p => p.Id == imported.Id); message.Text = "Profile saved. Preview it to check exact packages and compatibility; nothing has been applied."; break;
                case "Export":
                    if (selected is null) return; var save = new SaveFileDialog { Filter = "Mod profile (*.qdsprofile)|*.qdsprofile", FileName = "profile.qdsprofile", Title = "Export profile metadata" }; if (save.ShowDialog(this) is true) File.WriteAllBytes(save.FileName, Data.Bytes(selected)); break;
            }
            RefreshSidebar();
        }
        catch (Exception error) { RecordError(error); }
    }
    private string? PromptName(string title, string value)
    {
        var dialog = new Window { Owner = this, Title = title, Width = 400, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(20) }; dialog.Content = panel;
        panel.Children.Add(Muted("Profile name", new Thickness(0, 0, 0, 8))); var input = new TextBox { Text = value, MaxLength = 100 }; System.Windows.Automation.AutomationProperties.SetName(input, "Profile name"); panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) }; var cancelName = Button("Cancel"); cancelName.IsCancel = true; var save = Button("Save"); save.IsDefault = true; buttons.Children.Add(cancelName); buttons.Children.Add(save); panel.Children.Add(buttons);
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true; }; dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() is true ? input.Text.Trim() : null;
    }
    private async Task ApplyProfile(ModProfile profile, bool? suspended = null, string action = "Profile")
    {
        if (busy || inspection is null) return;
        string chosenTarget = target.Text; var before = inspection;
        try
        {
            profile.RequireCompatible(); busy = true; using var cancellation = new CancellationTokenSource(); network = cancellation; Buttons();
            var missing = profile.Mods.Where(pin => !packages.Any(p => p.ArchiveHash == pin.Sha256)).ToArray();
            // Resolve every pin before starting any download or installation change.
            var downloads = missing.Select(pin =>
            {
                var item = snapshot?.Document.Mods.FirstOrDefault(m => m.Id == pin.Id); var release = item?.Releases.FirstOrDefault(r => r.Version == pin.Version && r.Sha256 == pin.Sha256 && IsCompatible(r));
                if (item is null || release is null) throw new InvalidOperationException("Missing exact package: " + pin.Id + " " + pin.Version + ". Import its verified .qdsmod package and preview again.");
                return (item, release);
            }).ToArray();
            foreach (var (item, release) in downloads)
            { message.Text = "Verifying " + item.Name + " " + release.Version + "…"; var package = await online.Download(item, release, Path.Combine(userData, "Packages"), cancellation.Token); packages.Add(package); }
            cancellation.Token.ThrowIfCancellationRequested();
            var plan = new Manager(chosenTarget).PrepareProfile(profile, packages, suspended ?? before.Suspended, action);
            if (plan.ExpectedStamp != before.Stamp) throw new InvalidOperationException("The installation changed while preparing packages. Refresh and preview again.");
            string detail = string.Join("\n", Catalog.Mods.Select(mod =>
            {
                var old = before.Profile?.Mods.FirstOrDefault(m => m.Id == mod.Id); var next = profile.Mods.FirstOrDefault(m => m.Id == mod.Id);
                string State(ProfileMod? pin) => pin is null ? "not managed" : pin.Version + (pin.Enabled ? " · enabled" : " · disabled");
                return mod.Name + ": " + State(old) + " → " + State(next);
            })) + "\n\nManaged mods: " + (plan.Group!.Suspended ? "off (profile choices retained)" : "on") + "\n" + plan.ChangedFiles.Length + (plan.ChangedFiles.Length == 1 ? " file changes" : " files change") + " in one undoable operation. Unmanaged files remain.\nOther installations keep their applied configuration.";
            busy = false; network = null; Buttons();
            await ExecutePlan(plan, action switch { "Profile" => "Apply profile “" + profile.Name + "”", "Updates" => "Update managed mods", "Suspend" => "Turn managed mods off", _ => "Turn managed mods on" }, detail);
        }
        catch (OperationCanceledException) { message.Text = "Canceled. The installation and active profile are unchanged."; }
        catch (Exception error) { RecordError(error); }
        finally { busy = false; network = null; Buttons(); }
    }
    private (CatalogItem Item, CatalogRelease Release, ProfileMod Pin)[] AvailableUpdates()
    {
        if (inspection?.Profile is not { } profile || snapshot is null) return [];
        return profile.Mods.Select(pin =>
        {
            var item = snapshot.Document.Mods.FirstOrDefault(i => i.Id == pin.Id); var release = item?.Releases.Where(IsCompatible).OrderByDescending(r => Version.Parse(r.Version)).FirstOrDefault();
            return (Item: item!, Release: release!, Pin: pin);
        }).Where(x => x.Item is not null && x.Release is not null && Version.Parse(x.Release.Version) > Version.Parse(x.Pin.Version)).ToArray();
    }
    private void ReviewUpdates()
    {
        var available = AvailableUpdates(); if (busy || available.Length == 0 || inspection?.Profile is not { } profile) return;
        var dialog = new Window { Owner = this, Title = "Mod updates", Width = Math.Min(660, ActualWidth), Height = Math.Min(550, ActualHeight), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(20) }; dialog.Content = panel;
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var boxes = new List<(CheckBox Box, string Id)>(); var rows = new StackPanel(); panel.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        rows.Children.Add(new TextBlock { Text = "Review mod updates", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        foreach (var item in available)
        { var box = new CheckBox { Content = item.Item.Name + "  " + item.Pin.Version + " → " + item.Release.Version, IsChecked = true, Margin = new Thickness(0, 8, 0, 4) }; boxes.Add((box, item.Pin.Id)); rows.Children.Add(box); rows.Children.Add(Muted(Notes(item.Pin.Id, item.Release.Version), new Thickness(24, 0, 0, 12))); }
        string[]? chosen = null; var cancelUpdates = Button("Cancel"); cancelUpdates.IsCancel = true; actions.Children.Add(cancelUpdates);
        foreach (string label in new[] { "Update Selected", "Update All" }) { var button = Button(label); actions.Children.Add(button); button.Click += (_, _) => { chosen = boxes.Where(b => label == "Update All" || b.Box.IsChecked is true).Select(b => b.Id).ToArray(); if (chosen.Length > 0) dialog.DialogResult = true; }; }
        if (dialog.ShowDialog() is true && chosen is not null)
        {
            var next = profile with { Mods = profile.Mods.Select(pin => { var update = available.FirstOrDefault(a => a.Pin.Id == pin.Id); return chosen.Contains(pin.Id) ? pin with { Version = update.Release.Version, Sha256 = update.Release.Sha256 } : pin; }).ToArray() };
            _ = ApplyProfile(next, inspection.Suspended, "Updates");
        }
    }
    private string Notes(string id, string? version) => presentation?.Mods.FirstOrDefault(m => m.Id == id)?.ReleaseNotes.GetValueOrDefault(version ?? "") ?? "Release notes are not cached for this version. View the release history in Source & help on GitHub.";
    private void ShowPresentation(ModRow? row)
    {
        var data = presentation?.Mods.FirstOrDefault(m => m.Id == row?.Id);
        string username = data?.Author ?? "";
        // Old cached metadata contains a display name. Fall back to the trusted
        // repository owner until the username metadata has refreshed.
        if (row is not null && !System.Text.RegularExpressions.Regex.IsMatch(username, @"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$")) username = OnlineCatalog.Repository(row.Id).Split('/')[0];
        author.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        authorLink.Inlines.Clear(); authorLink.Inlines.Add(username);
        authorLink.NavigateUri = row is null ? null : new Uri("https://github.com/" + username);
        System.Windows.Automation.AutomationProperties.SetName(authorLink, username + " on GitHub"); authorLink.ToolTip = "Open " + username + " on GitHub";
        releaseNotes.Text = row is null ? "" : Notes(row.Id, (versions.SelectedItem as VersionChoice)?.Version);
        detailImage.Source = row is null ? null : thumbnails.GetValueOrDefault(row.Id); detailImage.Visibility = detailImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private void Launch()
    {
        if (busy) return;
        if (smoke) { message.Text = "Preview mode: Designer is not launched."; return; }
        try { var current = new Manager(target.Text).Inspect(packages); if (current.Pending is not null) throw new InvalidOperationException("Recover the interrupted operation before launching Designer."); Open(Path.Combine(current.Target, "Q-Sys Designer.exe")); }
        catch (Exception error) { RecordError(error); }
    }
    private string ModList() => AppIdentity.Name + " " + AppUpdate.CurrentVersion + "\n" + target.Text + "\nProfile: " + inspection?.Profile?.Name + "\nManaged mods: " + (inspection?.Suspended is true ? "off" : "on") + "\n" + string.Join("\n", inspection?.Mods.Select(m => m.Name + " " + m.Version + " · " + m.Status) ?? []) + "\nUnmanaged files: " + string.Join(", ", inspection?.Unmanaged ?? []);
    private void Settings()
    {
        if (busy) return;
        var dialog = new Window { Owner = this, Title = "Settings & diagnostics", Width = Math.Min(610, ActualWidth), Height = Math.Min(560, ActualHeight), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(22) }; dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Settings & diagnostics", FontSize = 22, FontWeight = FontWeights.SemiBold }); panel.Children.Add(Muted("Appearance", new Thickness(0, 18, 0, 8))); panel.Children.Add(theme);
        panel.Children.Add(Separator()); panel.Children.Add(Muted("MANAGER UPDATE", new Thickness(0, 0, 0, 8))); panel.Children.Add(updateStatus); panel.Children.Add(checkUpdate); panel.Children.Add(installUpdate);
        panel.Children.Add(Separator()); panel.Children.Add(backups); var copy = Button("Copy mod list"); panel.Children.Add(copy); copy.Click += (_, _) => { try { Clipboard.SetText(ModList()); message.Text = "Mod list copied."; } catch (Exception e) { RecordError(e); } };
        var export = Button("Export local diagnostic report…"); panel.Children.Add(export); export.Click += (_, _) =>
        {
            try
            {
                var save = new SaveFileDialog { Title = "Save local diagnostic report", FileName = "qsys-mod-manager-diagnostics.json", Filter = "JSON report (*.json)|*.json" }; if (save.ShowDialog(dialog) is not true) return;
                File.WriteAllBytes(save.FileName, Data.Bytes(new { schema = 1, createdUtc = DateTimeOffset.UtcNow, managerVersion = AppUpdate.CurrentVersion, os = Environment.OSVersion.ToString(), designer = target.Text, compatibility = compatibility.Text, hostSha256 = hostHash, commonSha256 = commonHash, inspection, catalogStatus = catalogStatus.Text, errors = errors.Concat(inspection is null ? [] : new Manager(target.Text).OperationErrors()).ToArray() })); message.Text = "Diagnostic report saved locally.";
            }
            catch (Exception e) { RecordError(e); }
        };
        panel.Children.Add(Muted("Diagnostics include local installation paths and operation errors. Nothing is uploaded.", new Thickness(0, 12, 0, 0)));
        dialog.ShowDialog(); panel.Children.Clear();
    }
}
