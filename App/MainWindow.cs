using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace QdsModManager.App;

internal sealed record ModRow(string Id, string Name, string Installed, string Available, string Status, ModStatus? Local, CatalogItem? Remote)
{
    public override string ToString() => Name + ", " + Status + "; installed " + Installed + ", available " + Available;
    public string Summary => Remote?.Description ?? Catalog.Mod(Id).Description;
    public string VersionSummary => Status + " · " + (Installed == "—" ? "Available " + Available : Installed == Available ? "Version " + Installed : Installed + " → " + Available);
    public string ActionName => "Actions for " + Name;
    public ImageSource? Thumbnail { get; init; }
    public string PrimaryLabel => Local?.Managed is true ? (Local.Enabled ? "Disable " : "Enable ") + Name : Local?.Status == "Installed · not managed yet" ? "Manage existing " + Name : "Install " + Name;
    public string RemoveLabel => "Remove " + Name;
    public bool CanManage { get; init; }
    public bool CanEnable { get; init; } = true;
    public bool Selected { get; set; }
    public string SelectionLabel => "Select " + Name;
    public bool PrimaryEnabled => CanManage && (Local?.Managed is true && Local.Enabled || CanEnable && Available != "—") && Local?.Status is not ("Unrecognized installed files" or "Files changed outside manager") && Status != "Not compatible";
    public bool RemoveEnabled => CanManage && Local?.Managed is true;
    public Geometry PrimaryIcon => (Geometry)Application.Current.FindResource("Fluent" + (Local?.Managed is true ? Local.Enabled ? "Prohibited" : "CheckmarkCircle" : Local?.Status == "Installed · not managed yet" ? "Settings" : "ArrowDownload"));
}
internal sealed record VersionChoice(string Label, string Version, ModPackage? Package, CatalogRelease? Release, CatalogItem? Item) { public override string ToString() => Label; }
internal sealed record ThemeChoice(string Name, Appearance Value) { public override string ToString() => Name; }

internal sealed partial class MainWindow : Window
{
    private readonly ComboBox target = new() { MinWidth = 0, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox theme = new() { Width = 170, DisplayMemberPath = "Name" };
    private readonly ComboBox scope = new() { Width = 144, Height = 36, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBox search = new() { MinWidth = 160, Height = 36, Tag = "Search mods…", ToolTip = "Search mod names and descriptions" };
    private readonly DataGrid mods = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, RowHeight = 88, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
    private readonly ComboBox versions = new() { Margin = new Thickness(0, 8, 0, 16), DisplayMemberPath = "Label" };
    private readonly TextBlock heading = new() { FontSize = 24, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock description = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16), LineHeight = 22 };
    private readonly TextBlock compatibility = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock availability = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16), LineHeight = 20 };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), LineHeight = 22 };
    private readonly TextBlock catalogStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock updateStatus = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 0) };
    private readonly Button browse = Button("Browse…");
    private readonly ScrollViewer detailScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Button checkUpdate = Button("Check for updates"), installUpdate = Button("Install update");
    private ManagerRelease? managerUpdate;
    private bool updateChecking, updateHandoff;
    private readonly Button install = Button("Install"), adopt = Button("Manage existing"), toggle = Button("Disable"), remove = Button("Remove"), undo = Button("Undo last change"), refreshCatalog = Button("Refresh catalog"), cancel = Button("Cancel");
    private readonly TextBlock repository = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Hyperlink repositoryLink = new();
    private readonly System.Windows.Shapes.Path repositoryIcon = new() { Width = 16, Height = 16, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly string userData;
    private readonly List<ModPackage> packages = new();
    private readonly Themes themes;
    private readonly OnlineCatalog online;
    private readonly Func<CancellationToken, Task<ManagerRelease>> managerRelease;
    private CatalogSnapshot? snapshot;
    private Inspection? inspection;
    private CancellationTokenSource? network;
    private bool refreshing, busy, applying, smoke;
    private string? hostHash, commonHash;
    private string libraryErrors = "";

    internal MainWindow(string? dataDirectory = null, HttpClient? catalogClient = null, Func<CancellationToken, Task<ManagerRelease>>? managerRelease = null)
    {
        userData = dataDirectory ?? AppIdentity.UserData;
        Title = AppIdentity.Name + " · " + AppUpdate.CurrentVersion; Width = 1280; Height = 740; MinWidth = 680; MinHeight = 360;
        Icon = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri("pack://application:,,,/" + typeof(MainWindow).Assembly.GetName().Name + ";component/Assets/qmm.ico"), System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames.OrderByDescending(frame => frame.PixelWidth).First();
        themes = new Themes(this, Path.Combine(userData, "settings.json")); online = new OnlineCatalog(userData, catalogClient);
        this.managerRelease = managerRelease ?? LatestManagerRelease;
        SetResourceReference(BackgroundProperty, "Page"); SetResourceReference(ForegroundProperty, "Text"); FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 14;
        try { online.LoadCached(); snapshot = new(DateTimeOffset.UtcNow, Catalog.Current, true); } catch { /* Invalid caches never expand trusted package permissions. */ }
        BuildLayout();
        theme.ItemsSource = new[] { new ThemeChoice("Follow System", Appearance.System), new ThemeChoice("Light", Appearance.Light), new ThemeChoice("Dark", Appearance.Dark) };
        theme.SelectedItem = theme.Items.Cast<ThemeChoice>().Single(t => t.Value == themes.Settings.Theme);
        theme.SelectionChanged += (_, _) => { try { if (theme.SelectedItem is ThemeChoice choice) themes.Select(choice.Value, !smoke); } catch (Exception e) { message.Text = "Could not save theme: " + e.Message; } };
        search.TextChanged += (_, _) => Filter(); scope.SelectionChanged += (_, _) => Filter();
        refresh.Click += (_, _) => Refresh(); target.SelectionChanged += (_, _) => { if (IsLoaded && !refreshing) Dispatcher.BeginInvoke(Refresh); };
        browse.Click += (_, _) => { if (busy) return; var picker = new OpenFolderDialog { Title = "Choose Q-SYS Designer" }; if (RunDialog(() => picker.ShowDialog(this)) is true) SelectTarget(picker.FolderName); };
        mods.SelectionChanged += (_, _) => ShowMod(); versions.SelectionChanged += (_, _) => Buttons();
        install.Click += async (_, _) => await Install(); adopt.Click += async (_, _) => await Change("Adopt");
        toggle.Click += async (_, _) => await Change((mods.SelectedItem as ModRow)?.Local?.Enabled is true ? "Disable" : "Enable");
        remove.Click += async (_, _) => await Change("Remove"); undo.Click += async (_, _) => await Change(inspection?.Pending is not null ? "Recover" : "Undo");
        refreshCatalog.Click += async (_, _) => await FetchCatalog(); import.Click += (_, _) => Import();
        checkUpdate.Click += async (_, _) => await CheckManagerUpdate(); installUpdate.Click += async (_, _) => await InstallManagerUpdate();
        backups.Click += (_, _) => { try { var path = Path.Combine(target.Text, ".qds-mod-manager"); if (Directory.Exists(path)) Open(path); else message.Text = "Backups appear after your first change."; } catch (Exception e) { message.Text = e.Message; } };
        repositoryLink.RequestNavigate += (_, args) => { Open(args.Uri.AbsoluteUri); args.Handled = true; };
        Closing += (_, args) => { if (applying && !updateHandoff) { args.Cancel = true; message.Text = "Please wait for the current change to finish."; } };
        Closed += (_, _) => { closed = true; lifetime.Cancel(); network?.Cancel(); themes.Dispose(); online.Dispose(); };
        Loaded += async (_, _) => await Initialize(false);
    }
    private static Button Button(string text) => new() { Content = text };
    private void SetButtonIcon(Button button, string icon)
    {
        button.Resources["ActionIcon"] = FindResource("Fluent" + icon);
        button.ContentTemplate = (DataTemplate)FindResource("ActionButtonContent");
    }
    private static TextBlock Muted(string text, Thickness margin) { var label = new TextBlock { Text = text, Margin = margin, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }; label.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return label; }
    private static Border Card(UIElement child) { var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Child = child }; card.SetResourceReference(BackgroundProperty, "Card"); return card; }
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    internal void SelectTarget(string path) { refreshing = true; if (!target.Items.Contains(path)) target.Items.Add(path); target.SelectedItem = path; refreshing = false; Refresh(); }
    private bool IsCompatible(CatalogRelease release) => OnlineCatalog.Compatible(release, hostHash, commonHash);
    private bool CanInstall(CatalogRelease release) => inspection is not null && PackageCompatibility.CanUse(release.HostSha256, release.CommonSha256, hostHash, commonHash);
    private bool CanInstall(ModPackage package) => inspection is not null && PackageCompatibility.CanUse(package.Manifest.HostSha256, package.Manifest.CommonSha256, hostHash, commonHash);
    private bool IsCompatible(ModPackage package) => PackageCompatibility.Exact(package.Manifest.HostSha256, package.Manifest.CommonSha256, hostHash, commonHash);
    private void LoadLibrary()
    {
        packages.Clear(); var errors = new List<string>();
        foreach (string directory in new[] { Path.Combine(AppContext.BaseDirectory, "Packages"), Path.Combine(userData, "Packages") })
            if (Directory.Exists(directory)) foreach (var file in Directory.GetFiles(directory, "*.qdsmod"))
                try { var package = ModPackage.Open(file); if (!packages.Any(p => p.ArchiveHash == package.ArchiveHash)) packages.Add(package); }
                catch (Exception error) { errors.Add(Path.GetFileName(file) + ": " + error.Message); }
        libraryErrors = string.Join("\n", errors);
    }
    private void Refresh()
    {
        if (busy || refreshing) return; refreshing = true; hostHash = commonHash = null;
        // An in-place Designer update can keep the same installation path.
        RefreshInstallationLabels();
        try
        {
            LoadLibrary(); var engine = new Manager(target.Text); packages.AddRange(engine.CachedPackages().Where(p => !packages.Any(x => x.ArchiveHash == p.ArchiveHash))); inspection = engine.Inspect(packages); RefreshProfiles(); hostHash = inspection.Build?.HostSha256; commonHash = inspection.Build?.CommonSha256;
            if (!smoke && themes.Settings.DesignerPath != inspection.Target)
                try { themes.Save(themes.Settings with { DesignerPath = inspection.Target }); } catch (Exception error) { errors.Add("Could not remember Designer: " + error.Message); }
            compatibility.Text = inspection.Pending is not null ? "Recovery required" : inspection.Running ? "Designer is running" : "Ready to manage mods"; compatibility.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            if (inspection.Build?.Tested is false)
            {
                compatibility.Text = DesignerInstallation.Read(inspection.Target).Warning + (inspection.Pending is not null ? "\nRecovery required" : inspection.Running ? "\nDesigner is running" : "");
                compatibility.SetResourceReference(TextBlock.ForegroundProperty, "Bad");
            }
            message.Text = inspection.Pending is not null ? "An interrupted change needs recovery before you can continue." : "";
            if (libraryErrors.Length > 0) message.Text += "\nSome local packages could not be read: " + libraryErrors;
        }
        catch (Exception error) { inspection = null; compatibility.Text = error.GetBaseException().Message; compatibility.SetResourceReference(TextBlock.ForegroundProperty, "Bad"); message.Text = "Choose a complete Designer installation. Use the All filter to browse mods."; }
        finally { refreshing = false; Filter(); }
    }
    private void Filter()
    {
        if (refreshing || busy) return;
        string? selected = (mods.SelectedItem as ModRow)?.Id;
        var rows = Catalog.Mods.Select(mod =>
        {
            var local = inspection?.Mods.FirstOrDefault(m => m.Id == mod.Id); var remote = snapshot?.Document.Mods.FirstOrDefault(m => m.Id == mod.Id);
            var release = remote?.Releases.Where(CanInstall).OrderByDescending(r => Version.Parse(r.Version)).FirstOrDefault();
            string available = release?.Version ?? packages.Where(p => p.Manifest.Id == mod.Id && CanInstall(p)).OrderByDescending(p => Version.Parse(p.Manifest.Version)).FirstOrDefault()?.Manifest.Version ?? "—";
            string status = local?.Status ?? "Not compatible";
            if (local?.Managed is true && status != "Files changed outside manager" && release is not null && Version.Parse(release.Version) > Version.Parse(local.Version))
                status = Manager.RequiresSchematicToolsMigration(mod.Id, local.Version, release.Version) ? "Split migration available" : "Update available";
            var pin = inspection?.Profile?.Mods.FirstOrDefault(p => p.Id == mod.Id);
            bool canEnable = pin is null || packages.Any(p => p.ArchiveHash == pin.Sha256 && CanInstall(p));
            return new ModRow(mod.Id, mod.Name, local?.Version ?? "—", available, status, local, remote) { Selected = selectedModIds.Contains(mod.Id), Thumbnail = thumbnails.GetValueOrDefault(mod.Id) ?? Icon, CanManage = inspection is not null && inspection.Pending is null && !inspection.Suspended, CanEnable = canEnable };
        }).Where(row => (scope.SelectedIndex switch { 1 => inspection is not null && (row.Remote?.Releases.Any(IsCompatible) is true || packages.Any(p => p.Manifest.Id == row.Id && IsCompatible(p))), 2 => row.Local?.Managed is true || row.Local?.Enabled is true, 3 => row.Local?.Managed is true && row.Local.Enabled, 4 => row.Local?.Managed is true && row.Local.Enabled is false, 5 => row.Status == "Update available", _ => true })
            && OnlineCatalog.Matches(row.Remote ?? new(row.Id, row.Name, Catalog.Mod(row.Id).Description, OnlineCatalog.Repository(row.Id), Array.Empty<CatalogRelease>()), search.Text)).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        mods.ItemsSource = rows; mods.SelectedItem = rows.FirstOrDefault(m => m.Id == selected); ShowMod();
        emptyList.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshSidebar();
    }

    private void ShowMod()
    {
        if (refreshing) return;
        ArrangeDetails();
        var row = mods.SelectedItem as ModRow; heading.Text = row?.Name ?? "No matching mods";
        description.Text = row?.Remote?.Description ?? (row is null ? "Try a different search or use Reset." : Catalog.Mod(row.Id).Description);
        var choices = new List<VersionChoice>();
        if (row?.Remote is { } remote) foreach (var release in remote.Releases)
        {
            var package = packages.FirstOrDefault(p => p.ArchiveHash == release.Sha256);
            choices.Add(new(release.Version + (CanInstall(release) ? (package is null ? " · download" : " · downloaded") + (IsCompatible(release) ? "" : " · untested") : " · incompatible"), release.Version, package, release, remote));
        }
        foreach (var package in packages.Where(p => p.Manifest.Id == row?.Id && !choices.Any(c => c.Package?.ArchiveHash == p.ArchiveHash)))
            choices.Add(new(package.Manifest.Version + " · local" + (!CanInstall(package) ? " · incompatible" : !IsCompatible(package) ? " · untested" : ""), package.Manifest.Version, package, null, null));
        var ordered = choices.OrderByDescending(c => c.Release is null ? c.Package is not null && CanInstall(c.Package) : CanInstall(c.Release)).ThenByDescending(c => Version.Parse(c.Version)).ToArray();
        ShowPresentation(row);
        versions.ItemsSource = ordered; versions.SelectedItem = row?.Local?.Managed is false ? ordered.FirstOrDefault(c => c.Version == row.Local.Version) ?? ordered.FirstOrDefault() : ordered.FirstOrDefault(); Buttons();
    }
    private void Buttons()
    {
        migrate.Visibility = inspection?.Profile?.Mods.Any(p => p.Id == "schematic-tools" && Version.Parse(p.Version) < new Version(0,17,0)) is true ? Visibility.Visible : Visibility.Collapsed;
        migrate.IsEnabled = !busy && inspection is { Pending: null };
        var row = mods.SelectedItem as ModRow; var choice = versions.SelectedItem as VersionChoice;
        bool ready = !busy && inspection is not null && inspection.Pending is null && !inspection.Suspended;
        bool supported = choice is not null && (choice.Release is null ? choice.Package is not null && CanInstall(choice.Package) : CanInstall(choice.Release));
        bool alreadyInstalled = row?.Local?.Managed is true && row.Local.Enabled && row.Local.Version == choice?.Version
            && inspection?.Profile?.Mods.FirstOrDefault(p => p.Id == row.Id)?.Sha256 == (choice?.Package?.ArchiveHash ?? choice?.Release?.Sha256);
        install.Content = alreadyInstalled ? "Installed" : choice?.Package is null ? "Download & install" : row?.Local?.Managed is true ? "Install version" : "Install";
        SetButtonIcon(install, alreadyInstalled ? "Checkmark" : "ArrowDownload");
        install.IsEnabled = ready && supported && !alreadyInstalled;
        install.Visibility = alreadyInstalled ? Visibility.Collapsed : Visibility.Visible;
        adopt.IsEnabled = ready && supported && row?.Local?.Managed is false && row.Local.Status == "Installed · not managed yet" && choice?.Package is not null;
        adopt.Visibility = row?.Local?.Managed is false && row.Local.Status == "Installed · not managed yet" ? Visibility.Visible : Visibility.Collapsed;
        toggle.IsEnabled = remove.IsEnabled = ready && row?.Local?.Managed is true; toggle.Content = row?.Local?.Enabled is true ? "Disable" : "Enable";
        if (row?.Local?.Enabled is false && !row.CanEnable) toggle.IsEnabled = false;
        toggle.Visibility = remove.Visibility = row?.Local?.Managed is true ? Visibility.Visible : Visibility.Collapsed;
        SetButtonIcon(toggle, row?.Local?.Enabled is true ? "Prohibited" : "CheckmarkCircle");
        undo.Content = inspection?.Pending is not null ? "Recover interrupted change" : "Undo last change"; undo.IsEnabled = !busy && inspection is not null && (inspection.Pending is not null || inspection.CanUndo);
        target.IsEnabled = mods.IsEnabled = versions.IsEnabled = scope.IsEnabled = search.IsEnabled = refreshCatalog.IsEnabled = !busy;
        repositoryLink.IsEnabled = row is not null; repositoryLink.NavigateUri = row is null ? null :
            new Uri(presentation?.Mods.FirstOrDefault(m => m.Id == row.Id)?.HelpUrl ?? "https://github.com/" + OnlineCatalog.Repository(row.Id));
        repositoryLink.ToolTip = row is null ? "Select a mod to view its repository." : "Open " + row.Name + " on GitHub in your browser.";
        System.Windows.Automation.AutomationProperties.SetName(repositoryLink, row is null ? "View on GitHub" : "View " + row.Name + " on GitHub");
        RefreshSidebar();
        cancel.Visibility = busy && !applying ? Visibility.Visible : Visibility.Collapsed;
        checkUpdate.IsEnabled = !busy && !updateChecking && !checkingAll; installUpdate.IsEnabled = !busy && !updateChecking && !checkingAll && managerUpdate is not null;
        string? packageTarget = choice is null ? null : PackageCompatibility.Version(choice.Package?.Manifest.HostSha256 ?? choice.Release?.HostSha256, choice.Package?.Manifest.CommonSha256 ?? choice.Release?.CommonSha256);
        availability.Text = row is null ? "" : "Installed: " + row.Installed + "\nLatest available: " + row.Available
            + (packageTarget is null ? "" : "\nPackage target: Designer " + packageTarget)
            + "\n\n" + (!supported ? "This version is unavailable for the selected installation." : inspection?.Build?.Tested is false ? DesignerInstallation.Read(inspection.Target).Warning : "Verified for the tested Designer build.");
        availability.SetResourceReference(TextBlock.ForegroundProperty, supported && inspection?.Build?.Tested is true ? "Muted" : "Bad");
        string? blocked = busy ? "Wait for the current operation to finish." : inspection is null ? "Choose a complete Designer installation." : inspection.Pending is not null ? "Recover the interrupted change before managing mods." : row is null ? "Select a mod first." : null;
        Explain(install, blocked ?? (choice is null ? "Choose a version first." : !supported ? "This version is not available for your Designer." : alreadyInstalled ? "This version is already installed and enabled." : "Install the selected version. Changes take effect when Designer next starts."));
        Explain(adopt, blocked ?? (row?.Local?.Managed is true ? "This mod is already managed by Q-SYS Mod Manager." : row?.Local?.Status != "Installed · not managed yet" ? "This action is for an existing matching mod that is not managed yet." : choice?.Package is null ? "Import the matching mod package to register this installation." : "Register an existing matching installation without changing its files."));
        Explain(toggle, blocked ?? (toggle.IsEnabled ? inspection?.Running is true ? "Close Designer before changing this mod's enabled state." : "Changes take effect the next time Designer starts." : "Install or manage this mod before changing its enabled state."));
        Explain(remove, blocked ?? (remove.IsEnabled ? inspection?.Running is true ? "Close Designer before removing this mod." : "Remove this mod. Previous files are kept for undo." : "Install or manage this mod before removing it."));
        Explain(undo, busy ? "Wait for the current operation to finish." : inspection is null ? "Choose a complete Designer installation." : inspection.Pending is not null ? "Recover the interrupted change from its saved transaction." : inspection.CanUndo ? "Restore the setup before the last change." : "There is no managed change to undo yet.");
    }
    private static void Explain(Button button, string help) { button.ToolTip = help; System.Windows.Automation.AutomationProperties.SetHelpText(button, help); }
    private async Task FetchCatalog(bool artwork = true)
    {
        if (busy) return; catalogFresh = false; busy = true; using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); network = cancellation; catalogStatus.Text = "Checking available mods…"; Buttons();
        try { snapshot = await online.Fetch(cancellation.Token); catalogFresh = !snapshot.Offline; presentation = await online.FetchPresentation(cancellation.Token) ?? PresentationDocument.Bundled(); if (artwork) await LoadThumbnails(cancellation.Token); catalogStatus.Text = snapshot.Offline ? "Offline · saved catalog from " + snapshot.FetchedUtc.ToLocalTime().ToString("g") : "Catalog up to date · " + snapshot.Document.Mods.Length + " mods"; }
        catch (OperationCanceledException) { catalogStatus.Text = "Catalog refresh canceled."; }
        catch (Exception error) { catalogStatus.Text = "Catalog unavailable. Local packages still work. " + error.GetBaseException().Message; }
        finally { busy = false; network = null; Refresh(); Filter(); Buttons(); }
    }
    private async Task Install()
    {
        if (busy || versions.SelectedItem is not VersionChoice choice) return;
        await InstallChoices([choice]);
    }
    private bool Preview(string title, string details, string? location = null, string action = "Apply change", string? warning = null)
        => !Settings.PreviewChanges || Confirm(title, details, location, action, warning);
    private bool Confirm(string title, string details, string? location = null, string action = "Apply change", string? warning = null, string dialogTitle = "Preview change", bool allowCancel = true)
    {
        var dialog = new Window { Owner = this, Title = dialogTitle, Width = Math.Min(600, SystemParameters.WorkArea.Width - 24), Height = Math.Min(580, SystemParameters.WorkArea.Height - 48), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var frame = new DockPanel { Margin = new Thickness(20) }; dialog.Content = frame;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); frame.Children.Add(buttons);
        var panel = new StackPanel(); frame.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Muted(location ?? target.Text, new Thickness(0, 14, 0, 14)));
        if (warning is not null)
        {
            var notice = new TextBlock { Text = warning, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
            notice.SetResourceReference(TextBlock.ForegroundProperty, "Bad"); panel.Children.Add(notice);
        }
        panel.Children.Add(new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap, LineHeight = 23 });
        var back = Button("Cancel"); back.Tag = "Subtle"; back.IsCancel = true; var apply = Button(action); apply.Tag = "Primary"; apply.IsDefault = true; if (allowCancel) buttons.Children.Add(back); else apply.IsCancel = true; buttons.Children.Add(apply); apply.Click += (_, _) => dialog.DialogResult = true;
        return RunDialog(dialog.ShowDialog) is true;
    }
    private async Task Change(string action)
    {
        if (busy) return;
        if (action == "Install") { await Install(); return; }
        try
        {
            var row = mods.SelectedItem as ModRow; var choice = versions.SelectedItem as VersionChoice;
            if (action == "Enable" && row is not null)
            {
                var pin = inspection?.Profile?.Mods.Single(p => p.Id == row.Id);
                var package = packages.FirstOrDefault(p => p.ArchiveHash == pin?.Sha256) ?? throw new InvalidOperationException("Import the installed package before enabling this mod.");
                await InstallChoices([new(package.Manifest.Version, package.Manifest.Version, package, null, null)]); return;
            }
            var manager = new Manager(target.Text);
            var plan = manager.Prepare(action, row?.Id ?? "", choice?.Package?.Path);
            if (inspection is not null && plan.ExpectedStamp != inspection.Stamp) throw new InvalidOperationException("The installation changed. Refresh and try again.");
            string caption = action switch { "Adopt" => "Manage existing mod", "Undo" => "Undo last change", "Recover" => "Recover interrupted change", _ => action + " " + row?.Name };
            string detail = action == "Adopt" ? "Start tracking this installed version. Its files will stay the same." : $"{plan.ChangedFiles.Length} files will change. Previous files are kept for undo.";
            if (action is "Install" or "Adopt") detail = "Selected version: " + choice?.Version + "\nInstalled version: " + row?.Installed + "\n\n" + detail;
            if (plan.Group is not null) detail += "\n\n" + DescribeGroup(plan.Group.Profile);
            await ExecutePlan(plan, caption, detail);
        }
        catch (Exception error) { OperationError(error); }
    }
    private async Task ExecutePlan(OperationPlan plan, string caption, string detail)
    {
        if (plan.ChangedFiles.Length > 0 && !RequireDesignerClosed(plan.Target)) return;
        bool untested = plan.ExpectedBuild?.Tested is false;
        string? buildWarning = untested ? DesignerInstallation.Read(plan.Target).Warning : null;
        var accepted = inspection?.UntestedRuntime;
        bool alreadyAccepted = accepted is { Schema: 1 } && string.Equals(accepted.Target, plan.Target, StringComparison.OrdinalIgnoreCase)
            && accepted.HostSha256 == plan.ExpectedBuild?.HostSha256 && accepted.CommonSha256 == plan.ExpectedBuild?.CommonSha256;
        if (untested && !Settings.PreviewChanges && !alreadyAccepted
            && !Confirm("Use mods on this Designer version?", "This also allows Q-SYS Mods Core to start on this untested build. Mods may not load or work correctly.", plan.Target,
                "Continue", buildWarning, "Untested Designer version")) return;
        if (untested) detail += "\n\nContinuing allows enabled Q-SYS Mods Core to start on this untested build. Mods may not load or work correctly.";
        if (!Preview(caption, detail, plan.Target, warning: buildWarning)) return;
        plan = plan with { UntestedVersionAcknowledged = untested };
        if (smoke) { message.Text = "Preview mode: no installation changes are applied."; return; }
        try
        {
            busy = applying = true; Buttons(); message.Text = "Applying change…";
            string requests = Path.Combine(userData, "Requests"); Directory.CreateDirectory(requests);
            string token = Guid.NewGuid().ToString("N"), request = Path.Combine(requests, token + ".json"), resultPath = Path.Combine(requests, token + ".result.json");
            File.WriteAllBytes(request, Data.Bytes(plan));
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach (var arg in new[] { "--apply", request, "--result", resultPath }) start.ArgumentList.Add(arg);
            using var worker = Process.Start(start) ?? throw new IOException("Could not start the change helper."); await worker.WaitForExitAsync();
            if (!File.Exists(resultPath)) throw new IOException("The change helper ended without a receipt. Refresh to check for recovery.");
            var result = Data.Read<ApplyResult>(File.ReadAllBytes(resultPath)); busy = applying = false; Refresh(); message.Text = result.Message;
            if (!result.Success) OperationError(new InvalidOperationException(result.Message));
            else selectedModIds.Clear();
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 1223) { message.Text = "Change canceled. Administrator permission was not granted."; }
        catch (Exception error) { OperationError(error); }
        finally { busy = applying = false; string receiptMessage = message.Text; Refresh(); message.Text = receiptMessage; Buttons(); }
    }
    private void Import()
    {
        if (busy) return;
        var picker = new OpenFileDialog { Title = "Import a mod package", Filter = "QDS mod packages (*.qdsmod)|*.qdsmod", CheckFileExists = true };
        if (RunDialog(() => picker.ShowDialog(this)) is not true) return;
        try
        {
            var package = ModPackage.Open(picker.FileName); string directory = Path.Combine(userData, "Packages"); Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, package.ArchiveHash + ".qdsmod"); if (!File.Exists(destination)) File.Copy(package.Path, destination);
            if (Data.HashFile(destination) != package.ArchiveHash) throw new IOException("Imported package changed during copying.");
            Refresh(); message.Text = package.Manifest.Name + " " + package.Manifest.Version + " imported. Select it to install.";
        }
        catch (Exception error) { OperationError(error); }
    }
    internal void SmokeSetup(string appearance, CatalogDocument? document)
    {
        smoke = true; themes.Select(Enum.Parse<Appearance>(appearance), false);
        theme.SelectedItem = theme.Items.Cast<ThemeChoice>().Single(t => t.Value == themes.Settings.Theme);
        if (document is not null) { snapshot = new(DateTimeOffset.UtcNow, OnlineCatalog.Validate(document)); catalogStatus.Text = "Catalog up to date · " + document.Mods.Length + " mods"; }
    }
    internal object SmokeResult() => new { compatible = inspection is not null, rows = mods.Items.Count, packages = packages.Count, running = inspection?.Running, theme = themes.Settings.Theme.ToString(), dark = themes.Dark, mods = mods.Items.Cast<ModRow>().Select(m => new { m.Id, m.Name, m.Status, m.Installed, m.Available }).ToArray(), message = message.Text };
    internal Task SmokeFetch() => FetchCatalog();
    private async Task CheckManagerUpdate()
    {
        if (busy || updateChecking) return; updateChecking = true; Buttons(); updateStatus.Text = "Checking for a Q-SYS Mod Manager update…";
        try
        {
            var release = await managerRelease(lifetime.Token);
            ShowManagerRelease(release);
        }
        catch (Exception error) { managerUpdate = null; installUpdate.Visibility = Visibility.Collapsed; updateStatus.Text = "Couldn't check for updates. " + error.GetBaseException().Message; }
        finally { updateChecking = false; Buttons(); }
    }
    private static async Task<ManagerRelease> LatestManagerRelease(CancellationToken cancellation)
    {
        using var client = new ManagerReleaseClient(); return await client.Latest(cancellation);
    }
    private void ShowManagerRelease(ManagerRelease release)
    {
        managerUpdate = release.IsNewerThan(AppUpdate.CurrentVersion) ? release : null;
        updateStatus.Text = "Installed " + AppUpdate.CurrentVersion + (managerUpdate is null ? " · Up to date" : "");
        installUpdate.Content = "Install " + release.Version; installUpdate.Visibility = managerUpdate is null ? Visibility.Collapsed : Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetName(installUpdate, "Install Manager " + release.Version);
        UpdateStateChanged?.Invoke();
    }
    private async Task InstallManagerUpdate()
    {
        if (busy || updateChecking || checkingAll || managerUpdate is not { } release) return;
        if (!Preview("Update Q-SYS Mod Manager to " + release.Version, "Q-SYS Mod Manager will restart after installation. Your settings, mod packages and installed mods will be kept. Q-SYS Designer can stay open.", AppContext.BaseDirectory, "Update & restart")) return;
        if (smoke) { message.Text = "Preview mode: no Manager update is downloaded or installed."; return; }
        busy = true; using var cancellation = new CancellationTokenSource(); network = cancellation; Buttons(); message.Text = "Downloading Q-SYS Mod Manager " + release.Version + "…";
        try
        {
            string folder = await AppUpdate.Stage(release, cancellation.Token);
            applying = true; network = null; Buttons(); message.Text = "Allow the administrator prompt to install the update. Preparing to restart…";
            using var watcher = AppUpdate.StartWatcher(folder); await AppUpdate.WaitForHandoff(folder, watcher);
            updateHandoff = true; Application.Current.Shutdown();
        }
        catch (OperationCanceledException) { message.Text = "Update download canceled. Nothing was installed."; }
        catch (Exception error) { OperationError(error); }
        finally { busy = applying = false; network = null; Buttons(); }
    }
}
