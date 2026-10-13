using System.Diagnostics;
using System.IO;
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
    public string Summary => Remote?.Description ?? Catalog.Mod(Id).Description;
    public string VersionSummary => "Installed " + Installed + "  →  Available " + Available + " · " + (Status == "Not compatible" ? "Unsupported build" : "Designer 10.5");
    public string ActionName => "Actions for " + Name;
    public ImageSource? Thumbnail { get; init; }
    public string PrimaryLabel => Local?.Managed is true ? (Local.Enabled ? "Disable " : "Enable ") + Name : Local?.Status == "Installed · not managed yet" ? "Manage existing " + Name : "Install " + Name;
    public string RemoveLabel => "Remove " + Name;
    public bool CanManage { get; init; }
    public bool PrimaryEnabled => CanManage && Available != "—" && Local?.Status is not ("Unrecognized installed files" or "Files changed outside manager") && Status != "Not compatible";
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
    private readonly DataGrid mods = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, RowHeight = 102, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
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
    private CatalogSnapshot? snapshot;
    private Inspection? inspection;
    private CancellationTokenSource? network;
    private bool refreshing, busy, applying, smoke;
    private string? hostHash, commonHash;
    private string libraryErrors = "";

    internal MainWindow(string? dataDirectory = null)
    {
        userData = dataDirectory ?? AppIdentity.UserData;
        Title = AppIdentity.Name + " · " + AppUpdate.CurrentVersion; Width = 1280; Height = 740; MinWidth = 680; MinHeight = 360;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/qmm.ico"));
        themes = new Themes(this, Path.Combine(userData, "settings.json")); online = new OnlineCatalog(userData);
        SetResourceReference(BackgroundProperty, "Page"); SetResourceReference(ForegroundProperty, "Text"); FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 14;
        BuildLayout();
        theme.ItemsSource = new[] { new ThemeChoice("Follow System", Appearance.System), new ThemeChoice("Light", Appearance.Light), new ThemeChoice("Dark", Appearance.Dark) };
        theme.SelectedItem = theme.Items.Cast<ThemeChoice>().Single(t => t.Value == themes.Settings.Theme);
        theme.SelectionChanged += (_, _) => { try { if (theme.SelectedItem is ThemeChoice choice) themes.Select(choice.Value, !smoke); } catch (Exception e) { message.Text = "Could not save theme: " + e.Message; } };
        search.TextChanged += (_, _) => Filter(); scope.SelectionChanged += (_, _) => Filter();
        refresh.Click += (_, _) => Refresh(); target.SelectionChanged += (_, _) => { if (IsLoaded && !refreshing) Dispatcher.BeginInvoke(Refresh); };
        browse.Click += (_, _) => { if (busy) return; var picker = new OpenFolderDialog { Title = "Choose Q-SYS Designer" }; if (picker.ShowDialog(this) is true) SelectTarget(picker.FolderName); };
        mods.SelectionChanged += (_, _) => ShowMod(); versions.SelectionChanged += (_, _) => Buttons();
        install.Click += async (_, _) => await Install(); adopt.Click += async (_, _) => await Change("Adopt");
        toggle.Click += async (_, _) => await Change((mods.SelectedItem as ModRow)?.Local?.Enabled is true ? "Disable" : "Enable");
        remove.Click += async (_, _) => await Change("Remove"); undo.Click += async (_, _) => await Change(inspection?.Pending is not null ? "Recover" : "Undo");
        refreshCatalog.Click += async (_, _) => await FetchCatalog(); import.Click += (_, _) => Import();
        checkUpdate.Click += async (_, _) => await CheckManagerUpdate(); installUpdate.Click += async (_, _) => await InstallManagerUpdate();
        backups.Click += (_, _) => { try { var path = Path.Combine(target.Text, ".qds-mod-manager"); if (Directory.Exists(path)) Open(path); else message.Text = "Backups appear after your first change."; } catch (Exception e) { message.Text = e.Message; } };
        repositoryLink.RequestNavigate += (_, args) => { Open(args.Uri.AbsoluteUri); args.Handled = true; };
        Closing += (_, args) => { if (applying && !updateHandoff) { args.Cancel = true; message.Text = "Please wait for the current change to finish."; } else network?.Cancel(); };
        Closed += (_, _) => { themes.Dispose(); online.Dispose(); };
        Loaded += async (_, _) => { if (!smoke) { MigrateKnownInstallations(); if (target.Items.Count == 0) { foreach (var path in Manager.Discover()) target.Items.Add(path); if (target.Items.Count > 0) target.SelectedIndex = 0; } Refresh(); await FetchCatalog(); await CheckManagerUpdate(); var notice = AppUpdate.ConsumeNotice(); if (notice is not null) message.Text = notice; } };
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
        try
        {
            LoadLibrary(); var engine = new Manager(target.Text); packages.AddRange(engine.CachedPackages().Where(p => !packages.Any(x => x.ArchiveHash == p.ArchiveHash))); inspection = engine.Inspect(packages); RefreshProfiles(); hostHash = Catalog.HostHash; commonHash = Catalog.CommonHash;
            compatibility.Text = "Q-SYS Designer 10.5 · " + (inspection.Running ? "Designer is running" : "Ready to manage mods"); compatibility.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            message.Text = inspection.Pending is not null ? "An interrupted change needs recovery before you can continue." : "Choose a mod to see its available versions.";
            if (libraryErrors.Length > 0) message.Text += "\nSome local packages could not be read: " + libraryErrors;
        }
        catch (Exception error) { inspection = null; compatibility.Text = error.GetBaseException().Message; compatibility.SetResourceReference(TextBlock.ForegroundProperty, "Bad"); message.Text = "Choose a supported Designer installation. Use the All filter to browse mods."; }
        finally { refreshing = false; Filter(); }
    }
    private void Filter()
    {
        if (refreshing || busy) return;
        string? selected = (mods.SelectedItem as ModRow)?.Id;
        var rows = Catalog.Mods.Select(mod =>
        {
            var local = inspection?.Mods.FirstOrDefault(m => m.Id == mod.Id); var remote = snapshot?.Document.Mods.FirstOrDefault(m => m.Id == mod.Id);
            var release = remote?.Releases.Where(IsCompatible).OrderByDescending(r => Version.Parse(r.Version)).FirstOrDefault();
            string available = release?.Version ?? packages.Where(p => p.Manifest.Id == mod.Id).OrderByDescending(p => Version.Parse(p.Manifest.Version)).FirstOrDefault()?.Manifest.Version ?? "—";
            string status = local?.Status ?? "Not compatible";
            if (local?.Managed is true && status != "Files changed outside manager" && release is not null && Version.Parse(release.Version) > Version.Parse(local.Version)) status = "Update available";
            return new ModRow(mod.Id, mod.Name, local?.Version ?? "—", available, status, local, remote) { Thumbnail = thumbnails.GetValueOrDefault(mod.Id), CanManage = inspection is not null && inspection.Pending is null && !inspection.Suspended };
        }).Where(row => (scope.SelectedIndex switch { 1 => inspection is not null, 2 => row.Local?.Managed is true || row.Local?.Enabled is true, 3 => row.Local?.Enabled is true, 4 => row.Local?.Managed is true && row.Local.Enabled is false, 5 => row.Status == "Update available", _ => true })
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
            choices.Add(new(release.Version + (IsCompatible(release) ? package is null ? " · download" : " · downloaded" : " · incompatible"), release.Version, package, release, remote));
        }
        foreach (var package in packages.Where(p => p.Manifest.Id == row?.Id && !choices.Any(c => c.Package?.ArchiveHash == p.ArchiveHash)))
            choices.Add(new(package.Manifest.Version + " · local", package.Manifest.Version, package, null, null));
        var ordered = choices.OrderByDescending(c => c.Release is null ? inspection is not null : IsCompatible(c.Release)).ThenByDescending(c => Version.Parse(c.Version)).ToArray();
        ShowPresentation(row);
        versions.ItemsSource = ordered; versions.SelectedItem = row?.Local?.Managed is false ? ordered.FirstOrDefault(c => c.Version == row.Local.Version) ?? ordered.FirstOrDefault() : ordered.FirstOrDefault(); Buttons();
    }
    private void Buttons()
    {
        var row = mods.SelectedItem as ModRow; var choice = versions.SelectedItem as VersionChoice;
        bool ready = !busy && inspection is not null && inspection.Pending is null && !inspection.Suspended;
        bool supported = choice is not null && (choice.Release is null ? inspection is not null : IsCompatible(choice.Release));
        bool alreadyInstalled = row?.Local?.Managed is true && row.Local.Enabled && row.Local.Version == choice?.Version;
        install.Content = alreadyInstalled ? "Installed" : choice?.Package is null ? "Download & install" : row?.Local?.Managed is true ? "Install version" : "Install";
        SetButtonIcon(install, alreadyInstalled ? "Checkmark" : "ArrowDownload");
        install.IsEnabled = ready && supported && !alreadyInstalled;
        adopt.IsEnabled = ready && row?.Local?.Managed is false && row.Local.Status == "Installed · not managed yet" && choice?.Package is not null;
        toggle.IsEnabled = remove.IsEnabled = ready && row?.Local?.Managed is true; toggle.Content = row?.Local?.Enabled is true ? "Disable" : "Enable";
        SetButtonIcon(toggle, row?.Local?.Enabled is true ? "Prohibited" : "CheckmarkCircle");
        undo.Content = inspection?.Pending is not null ? "Recover interrupted change" : "Undo last change"; undo.IsEnabled = !busy && inspection is not null && (inspection.Pending is not null || inspection.CanUndo);
        target.IsEnabled = mods.IsEnabled = versions.IsEnabled = scope.IsEnabled = search.IsEnabled = refreshCatalog.IsEnabled = !busy;
        repositoryLink.IsEnabled = row is not null; repositoryLink.NavigateUri = row is null ? null : new Uri("https://github.com/" + OnlineCatalog.Repository(row.Id));
        repositoryLink.ToolTip = row is null ? "Select a mod to view its repository." : "Open " + row.Name + " on GitHub in your browser.";
        System.Windows.Automation.AutomationProperties.SetName(repositoryLink, row is null ? "View on GitHub" : "View " + row.Name + " on GitHub");
        RefreshSidebar();
        cancel.Visibility = busy && !applying ? Visibility.Visible : Visibility.Collapsed;
        checkUpdate.IsEnabled = !busy && !updateChecking; installUpdate.IsEnabled = !busy && !updateChecking && managerUpdate is not null;
        availability.Text = row is null ? "" : supported ? "Compatible with your Designer" : "This version is not compatible with the selected Designer.";
        availability.SetResourceReference(TextBlock.ForegroundProperty, supported ? "Muted" : "Bad");
        string? blocked = busy ? "Wait for the current operation to finish." : inspection is null ? "Choose a supported Designer installation." : inspection.Pending is not null ? "Recover the interrupted change before managing mods." : row is null ? "Select a mod first." : null;
        Explain(install, blocked ?? (choice is null ? "Choose a version first." : !supported ? "This version is not compatible with your Designer." : alreadyInstalled ? "This version is already installed and enabled." : "Install the selected version. You'll review the change before applying it."));
        Explain(adopt, blocked ?? (row?.Local?.Managed is true ? "This mod is already managed by Q-SYS Mod Manager." : row?.Local?.Status != "Installed · not managed yet" ? "This action is for an existing matching mod that is not managed yet." : choice?.Package is null ? "Import the matching mod package to register this installation." : "Register an existing matching installation without changing its files."));
        Explain(toggle, blocked ?? (toggle.IsEnabled ? inspection?.Running is true ? "Close Designer before changing this mod's enabled state." : "Changes take effect the next time Designer starts." : "Install or manage this mod before changing its enabled state."));
        Explain(remove, blocked ?? (remove.IsEnabled ? inspection?.Running is true ? "Close Designer before removing this mod." : "Remove this mod after reviewing the change. Previous files are kept for undo." : "Install or manage this mod before removing it."));
        Explain(undo, busy ? "Wait for the current operation to finish." : inspection is null ? "Choose a supported Designer installation." : inspection.Pending is not null ? "Recover the interrupted change from its saved transaction." : inspection.CanUndo ? "Restore the setup before the last change." : "There is no managed change to undo yet.");
    }
    private static void Explain(Button button, string help) { button.ToolTip = help; System.Windows.Automation.AutomationProperties.SetHelpText(button, help); }
    private async Task FetchCatalog()
    {
        if (busy) return; busy = true; using var cancellation = new CancellationTokenSource(); network = cancellation; Buttons(); catalogStatus.Text = "Checking available mods…";
        try { snapshot = await online.Fetch(cancellation.Token); presentation = await online.FetchPresentation(cancellation.Token) ?? PresentationDocument.Bundled(); await LoadThumbnails(cancellation.Token); catalogStatus.Text = snapshot.Offline ? "Offline · saved catalog from " + snapshot.FetchedUtc.ToLocalTime().ToString("g") : "Catalog up to date · " + snapshot.Document.Mods.Length + " mods"; }
        catch (OperationCanceledException) { catalogStatus.Text = "Catalog refresh canceled."; }
        catch (Exception error) { catalogStatus.Text = "Catalog unavailable. Local packages still work. " + error.GetBaseException().Message; }
        finally { busy = false; network = null; Filter(); Buttons(); }
    }
    private async Task Install()
    {
        if (busy || versions.SelectedItem is not VersionChoice choice) return;
        if (inspection?.Running is true) { message.Text = "Close this Designer installation, then refresh to install mods."; return; }
        if (choice.Package is null && choice.Item is not null && choice.Release is not null)
        {
            busy = true; using var cancellation = new CancellationTokenSource(); network = cancellation; Buttons(); message.Text = "Downloading " + choice.Item.Name + " " + choice.Version + "…";
            try
            {
                var package = await online.Download(choice.Item, choice.Release, Path.Combine(userData, "Packages"), cancellation.Token);
                busy = false; Refresh(); versions.SelectedItem = versions.Items.Cast<VersionChoice>().FirstOrDefault(c => c.Package?.ArchiveHash == package.ArchiveHash);
            }
            catch (OperationCanceledException) { message.Text = "Download canceled. Nothing was installed."; return; }
            catch (Exception error) { message.Text = error.GetBaseException().Message; return; }
            finally { busy = false; network = null; Buttons(); }
        }
        await Change("Install");
    }
    private bool Preview(string title, string details, string? location = null, string action = "Apply change")
    {
        var dialog = new Window { Owner = this, Title = "Preview change", Width = Math.Min(600, SystemParameters.WorkArea.Width - 24), Height = Math.Min(580, SystemParameters.WorkArea.Height - 48), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var frame = new DockPanel { Margin = new Thickness(20) }; dialog.Content = frame;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); frame.Children.Add(buttons);
        var panel = new StackPanel(); frame.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Muted(location ?? target.Text, new Thickness(0, 14, 0, 14)));
        panel.Children.Add(new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap, LineHeight = 23 });
        var back = Button("Cancel"); back.Tag = "Subtle"; back.IsCancel = true; var apply = Button(action); apply.Tag = "Primary"; apply.IsDefault = true; buttons.Children.Add(back); buttons.Children.Add(apply); apply.Click += (_, _) => dialog.DialogResult = true;
        return dialog.ShowDialog() is true;
    }
    private async Task Change(string action)
    {
        if (busy) return;
        try
        {
            var row = mods.SelectedItem as ModRow; var choice = versions.SelectedItem as VersionChoice;
            var plan = new Manager(target.Text).Prepare(action, row?.Id ?? "", choice?.Package?.Path);
            if (inspection is not null && plan.ExpectedStamp != inspection.Stamp) throw new InvalidOperationException("The installation changed. Refresh before previewing again.");
            if (plan.ChangedFiles.Length > 0 && inspection?.Running is true) { message.Text = "Close this Designer installation, then refresh to apply the change."; return; }
            string caption = action switch { "Adopt" => "Manage existing mod", "Undo" => "Undo last change", "Recover" => "Recover interrupted change", _ => action + " " + row?.Name };
            string detail = action == "Adopt" ? "Start tracking this installed version. Its files will stay the same." : $"{plan.ChangedFiles.Length} files will change. Previous files are kept for undo.";
            if (action is "Install" or "Adopt") detail = "Selected version: " + choice?.Version + "\nInstalled version: " + row?.Installed + "\n\n" + detail;
            await ExecutePlan(plan, caption, detail);
        }
        catch (Exception error) { RecordError(error); }
    }
    private async Task ExecutePlan(OperationPlan plan, string caption, string detail)
    {
        if (!Preview(caption, detail)) return;
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
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 1223) { message.Text = "Change canceled. Administrator permission was not granted."; }
        catch (Exception error) { RecordError(error); }
        finally { busy = applying = false; string receiptMessage = message.Text; Refresh(); message.Text = receiptMessage; Buttons(); }
    }
    private void Import()
    {
        if (busy) return;
        var picker = new OpenFileDialog { Title = "Import a mod package", Filter = "QDS mod packages (*.qdsmod)|*.qdsmod", CheckFileExists = true };
        if (picker.ShowDialog(this) is not true) return;
        try
        {
            var package = ModPackage.Open(picker.FileName); string directory = Path.Combine(userData, "Packages"); Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, package.ArchiveHash + ".qdsmod"); if (!File.Exists(destination)) File.Copy(package.Path, destination);
            if (Data.HashFile(destination) != package.ArchiveHash) throw new IOException("Imported package changed during copying.");
            Refresh(); message.Text = package.Manifest.Name + " " + package.Manifest.Version + " imported. Select it to install.";
        }
        catch (Exception error) { RecordError(error); }
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
            using var client = new ManagerReleaseClient(); var release = await client.Latest(default);
            ShowManagerRelease(release);
        }
        catch (Exception error) { managerUpdate = null; installUpdate.Visibility = Visibility.Collapsed; updateStatus.Text = "Couldn't check for updates. " + error.GetBaseException().Message; }
        finally { updateChecking = false; Buttons(); }
    }
    private void ShowManagerRelease(ManagerRelease release)
    {
        managerUpdate = release.IsNewerThan(AppUpdate.CurrentVersion) ? release : null;
        updateStatus.Text = managerUpdate is null ? AppIdentity.Name + " " + AppUpdate.CurrentVersion + " · Up to date" : AppIdentity.Name + " " + release.Version + " is available";
        installUpdate.Content = "Install " + release.Version; installUpdate.Visibility = managerUpdate is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private async Task InstallManagerUpdate()
    {
        if (busy || updateChecking || managerUpdate is not { } release) return;
        if (!Preview("Update Q-SYS Mod Manager to " + release.Version, "Q-SYS Mod Manager will restart after installation. Your settings, mod packages and installed mods will be kept. Q-SYS Designer can stay open.", AppContext.BaseDirectory, "Update & restart")) return;
        busy = true; using var cancellation = new CancellationTokenSource(); network = cancellation; Buttons(); message.Text = "Downloading Q-SYS Mod Manager " + release.Version + "…";
        try
        {
            string folder = await AppUpdate.Stage(release, cancellation.Token);
            applying = true; network = null; Buttons(); message.Text = "Allow the administrator prompt to install the update. Preparing to restart…";
            using var watcher = AppUpdate.StartWatcher(folder); await AppUpdate.WaitForHandoff(folder, watcher);
            updateHandoff = true; Application.Current.Shutdown();
        }
        catch (OperationCanceledException) { message.Text = "Update download canceled. Nothing was installed."; }
        catch (Exception error) { message.Text = error.GetBaseException().Message; }
        finally { busy = applying = false; network = null; Buttons(); }
    }
}
