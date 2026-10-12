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

internal sealed record ModRow(string Id, string Name, string Installed, string Available, string Status, ModStatus? Local, CatalogItem? Remote);
internal sealed record VersionChoice(string Label, string Version, ModPackage? Package, CatalogRelease? Release, CatalogItem? Item) { public override string ToString() => Label; }
internal sealed record ThemeChoice(string Name, Appearance Value) { public override string ToString() => Name; }

internal sealed class MainWindow : Window
{
    private readonly ComboBox target = new() { MinWidth = 320, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox theme = new() { Width = 170, DisplayMemberPath = "Name" };
    private readonly ComboBox scope = new() { Width = 144, Height = 36, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBox search = new() { MinWidth = 160, Height = 36, Tag = "Search mods…", ToolTip = "Search mod names and descriptions" };
    private readonly CheckBox compatibleOnly = new() { Content = "Compatible only", IsChecked = true, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly DataGrid mods = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, RowHeight = 48, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
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
    private readonly string userData = AppIdentity.UserData;
    private readonly List<ModPackage> packages = new();
    private readonly Themes themes;
    private readonly OnlineCatalog online;
    private CatalogSnapshot? snapshot;
    private Inspection? inspection;
    private CancellationTokenSource? network;
    private bool refreshing, busy, applying, smoke;
    private string? hostHash, commonHash;
    private string libraryErrors = "";

    internal MainWindow()
    {
        Title = AppIdentity.Name + " · " + AppUpdate.CurrentVersion; Width = 1220; Height = 860; MinWidth = 1060; MinHeight = 770;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/qmm.ico"));
        themes = new Themes(this, Path.Combine(userData, "settings.json")); online = new OnlineCatalog(userData);
        SetResourceReference(BackgroundProperty, "Page"); SetResourceReference(ForegroundProperty, "Text"); FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 14;
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var titleRow = new DockPanel(); top.Children.Add(titleRow);
        var appearance = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        appearance.Children.Add(Muted("Theme", new Thickness(0, 0, 12, 0))); appearance.Children.Add(theme); DockPanel.SetDock(appearance, Dock.Right); titleRow.Children.Add(appearance);
        titleRow.Children.Add(new TextBlock { Text = AppIdentity.Name, FontSize = 28, FontWeight = FontWeights.SemiBold });
        top.Children.Add(Muted("Find mods for your Designer.", new Thickness(0, 4, 0, 16)));
        top.Children.Add(Muted("Designer installation", new Thickness(0, 0, 0, 8)));
        var targetRow = new DockPanel(); top.Children.Add(targetRow);
        var refresh = Button("Refresh"); refresh.Margin = new Thickness(); browse.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(refresh, Dock.Right); targetRow.Children.Add(refresh); DockPanel.SetDock(browse, Dock.Right); targetRow.Children.Add(browse); targetRow.Children.Add(target);
        top.Children.Add(compatibility);

        var footer = new StackPanel(); var footerFrame = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 16, 0, 0), Margin = new Thickness(0, 16, 0, 0) }; footerFrame.SetResourceReference(Border.BorderBrushProperty, "Divider"); DockPanel.SetDock(footerFrame, Dock.Bottom); root.Children.Add(footerFrame);
        var updateRow = new DockPanel { Margin = new Thickness(0, 0, 0, 16) }; footer.Children.Add(updateRow);
        checkUpdate.Margin = new Thickness(); installUpdate.Margin = new Thickness(8, 0, 0, 0); installUpdate.Tag = "Primary";
        DockPanel.SetDock(installUpdate, Dock.Right); updateRow.Children.Add(installUpdate); DockPanel.SetDock(checkUpdate, Dock.Right); updateRow.Children.Add(checkUpdate); updateRow.Children.Add(updateStatus);
        installUpdate.Visibility = Visibility.Collapsed; updateStatus.Text = AppIdentity.Name + " " + AppUpdate.CurrentVersion; updateStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var footerButtons = new StackPanel { Orientation = Orientation.Horizontal }; footer.Children.Add(footerButtons);
        var import = Button("Import package…"); var backups = Button("Open backups");
        foreach (var button in new[] { refresh, refreshCatalog, checkUpdate, undo, backups, adopt, cancel }) button.Tag = "Subtle";
        foreach (var button in new[] { import, undo, backups, cancel }) footerButtons.Children.Add(button);
        cancel.Visibility = Visibility.Collapsed; cancel.Click += (_, _) => network?.Cancel(); footer.Children.Add(message);
        footer.Children.Add(Muted("Close Designer before changing mods. Changes take effect the next time it starts.", new Thickness(0, 8, 0, 0)));

        var body = new Grid { Margin = new Thickness(0, 24, 0, 0) }; root.Children.Add(body);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var listing = new DockPanel(); var listCard = Card(listing); listCard.Margin = new Thickness(0, 0, 16, 0); body.Children.Add(listCard);
        var filters = new DockPanel { Margin = new Thickness(0, 0, 0, 16) }; DockPanel.SetDock(filters, Dock.Top); listing.Children.Add(filters);
        scope.Items.Add("Browse mods"); scope.Items.Add("Installed mods"); scope.SelectedIndex = 0;
        DockPanel.SetDock(scope, Dock.Left); filters.Children.Add(scope); DockPanel.SetDock(compatibleOnly, Dock.Right); filters.Children.Add(compatibleOnly); filters.Children.Add(search);
        var catalogFooter = new DockPanel { Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(catalogFooter, Dock.Bottom); listing.Children.Add(catalogFooter);
        refreshCatalog.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(refreshCatalog, Dock.Right); catalogFooter.Children.Add(refreshCatalog); catalogStatus.Margin = new Thickness(); catalogStatus.VerticalAlignment = VerticalAlignment.Center; catalogFooter.Children.Add(catalogStatus); catalogStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        listing.Children.Add(mods);
        foreach (var column in new[] { ("Mod", "Name", 2.0), ("Installed", "Installed", 1.0), ("Available", "Available", 1.0), ("Status", "Status", 1.5) })
            mods.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2), Width = new DataGridLength(column.Item3, DataGridLengthUnitType.Star) });

        var detail = new StackPanel(); detailScroll.Content = detail; var detailCard = Card(detailScroll); Grid.SetColumn(detailCard, 1); body.Children.Add(detailCard);
        detail.Children.Add(heading); detail.Children.Add(description); detail.Children.Add(availability); detail.Children.Add(Muted("Version", new Thickness())); detail.Children.Add(versions);
        var actions = new Grid(); actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); detail.Children.Add(actions);
        var actionButtons = new[] { install, adopt, toggle, remove };
        for (int i = 0; i < actionButtons.Length; i++) { var button = actionButtons[i]; button.Margin = new Thickness(0, 0, i % 2 == 0 ? 8 : 0, 8); Grid.SetColumn(button, i % 2); Grid.SetRow(button, i / 2); actions.Children.Add(button); }
        install.Tag = "Primary";
        repositoryIcon.Data = (Geometry)FindResource("GitHubMark"); repositoryIcon.SetBinding(System.Windows.Shapes.Shape.FillProperty, new Binding(nameof(Hyperlink.Foreground)) { Source = repositoryLink }); repositoryIcon.IsHitTestVisible = false;
        repositoryLink.Inlines.Add(new InlineUIContainer(repositoryIcon) { BaselineAlignment = BaselineAlignment.Center }); repositoryLink.Inlines.Add(new Run("View on GitHub") { TextDecorations = TextDecorations.Underline }); repository.Inlines.Add(repositoryLink);
        repository.Margin = new Thickness(0, 16, 0, 0); repository.HorizontalAlignment = HorizontalAlignment.Left; detail.Children.Add(repository);
        foreach (var (control, name) in new (DependencyObject, string)[] { (target, "Designer installation"), (theme, "Theme"), (scope, "Mod list"), (search, "Search mods"), (versions, "Mod version"), (mods, "Available mods") }) System.Windows.Automation.AutomationProperties.SetName(control, name);

        theme.ItemsSource = new[] { new ThemeChoice("Follow System", Appearance.System), new ThemeChoice("Light", Appearance.Light), new ThemeChoice("Dark", Appearance.Dark) };
        theme.SelectedItem = theme.Items.Cast<ThemeChoice>().Single(t => t.Value == themes.Settings.Theme);
        theme.SelectionChanged += (_, _) => { try { if (theme.SelectedItem is ThemeChoice choice) themes.Select(choice.Value, !smoke); } catch (Exception e) { message.Text = "Could not save theme: " + e.Message; } };
        search.TextChanged += (_, _) => Filter(); scope.SelectionChanged += (_, _) => Filter(); compatibleOnly.Checked += (_, _) => Filter(); compatibleOnly.Unchecked += (_, _) => Filter();
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
        Loaded += async (_, _) => { if (target.Items.Count == 0) { foreach (var path in Manager.Discover()) target.Items.Add(path); if (target.Items.Count > 0) target.SelectedIndex = 0; } Refresh(); if (!smoke) { await FetchCatalog(); await CheckManagerUpdate(); var notice = AppUpdate.ConsumeNotice(); if (notice is not null) message.Text = notice; } };
    }
    private static Button Button(string text) => new() { Content = text };
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
            LoadLibrary(); inspection = new Manager(target.Text).Inspect(packages); hostHash = Catalog.HostHash; commonHash = Catalog.CommonHash;
            compatibility.Text = "Q-SYS Designer 10.5 · " + (inspection.Running ? "Designer is running" : "Ready to manage mods"); compatibility.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            message.Text = inspection.Pending is not null ? "An interrupted change needs recovery before you can continue." : "Choose a mod to see its available versions.";
            if (libraryErrors.Length > 0) message.Text += "\nSome local packages could not be read: " + libraryErrors;
        }
        catch (Exception error) { inspection = null; compatibility.Text = error.GetBaseException().Message; compatibility.SetResourceReference(TextBlock.ForegroundProperty, "Bad"); message.Text = "Choose a supported Designer installation. Turn off Compatible only to browse all mods."; }
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
            if (local?.Managed is true && release is not null && Version.Parse(release.Version) > Version.Parse(local.Version)) status = "Update available";
            return new ModRow(mod.Id, mod.Name, local?.Version ?? "—", available, status, local, remote);
        }).Where(row => (scope.SelectedIndex != 1 || row.Local?.Managed is true || row.Local?.Enabled is true)
            && (compatibleOnly.IsChecked != true || inspection is not null && (row.Remote is null || row.Remote.Releases.Any(IsCompatible)))
            && OnlineCatalog.Matches(row.Remote ?? new(row.Id, row.Name, Catalog.Mod(row.Id).Description, OnlineCatalog.Repository(row.Id), Array.Empty<CatalogRelease>()), search.Text)).ToArray();
        mods.ItemsSource = rows; mods.SelectedItem = rows.FirstOrDefault(m => m.Id == selected) ?? rows.FirstOrDefault(); ShowMod();
    }
    private void ShowMod()
    {
        if (refreshing) return;
        var row = mods.SelectedItem as ModRow; heading.Text = row?.Name ?? "No matching mods";
        description.Text = row?.Remote?.Description ?? (row is null ? "Try a different search or turn off Compatible only." : Catalog.Mod(row.Id).Description);
        var choices = new List<VersionChoice>();
        if (row?.Remote is { } remote) foreach (var release in remote.Releases)
        {
            var package = packages.FirstOrDefault(p => p.ArchiveHash == release.Sha256);
            choices.Add(new(release.Version + (IsCompatible(release) ? package is null ? " · download" : " · downloaded" : " · incompatible"), release.Version, package, release, remote));
        }
        foreach (var package in packages.Where(p => p.Manifest.Id == row?.Id && !choices.Any(c => c.Package?.ArchiveHash == p.ArchiveHash)))
            choices.Add(new(package.Manifest.Version + " · local", package.Manifest.Version, package, null, null));
        var ordered = choices.OrderByDescending(c => c.Release is null ? inspection is not null : IsCompatible(c.Release)).ThenByDescending(c => Version.Parse(c.Version)).ToArray();
        versions.ItemsSource = ordered; versions.SelectedItem = row?.Local?.Managed is false ? ordered.FirstOrDefault(c => c.Version == row.Local.Version) ?? ordered.FirstOrDefault() : ordered.FirstOrDefault(); Buttons();
    }
    private void Buttons()
    {
        var row = mods.SelectedItem as ModRow; var choice = versions.SelectedItem as VersionChoice;
        bool ready = !busy && inspection is not null && inspection.Pending is null;
        bool supported = choice is not null && (choice.Release is null ? inspection is not null : IsCompatible(choice.Release));
        bool alreadyInstalled = row?.Local?.Managed is true && row.Local.Enabled && row.Local.Version == choice?.Version;
        install.Content = alreadyInstalled ? "Installed" : choice?.Package is null ? "Download & install" : row?.Local?.Managed is true ? "Install version" : "Install";
        install.IsEnabled = ready && supported && !alreadyInstalled;
        adopt.IsEnabled = ready && row?.Local?.Managed is false && row.Local.Status == "Installed · not managed yet" && choice?.Package is not null;
        toggle.IsEnabled = remove.IsEnabled = ready && row?.Local?.Managed is true; toggle.Content = row?.Local?.Enabled is true ? "Disable" : "Enable";
        undo.Content = inspection?.Pending is not null ? "Recover interrupted change" : "Undo last change"; undo.IsEnabled = !busy && inspection is not null && (inspection.Pending is not null || inspection.CanUndo);
        target.IsEnabled = mods.IsEnabled = versions.IsEnabled = scope.IsEnabled = search.IsEnabled = compatibleOnly.IsEnabled = refreshCatalog.IsEnabled = !busy;
        repositoryLink.IsEnabled = row is not null; repositoryLink.NavigateUri = row is null ? null : new Uri("https://github.com/" + OnlineCatalog.Repository(row.Id));
        repositoryLink.ToolTip = row is null ? "Select a mod to view its repository." : "Open " + row.Name + " on GitHub in your browser.";
        System.Windows.Automation.AutomationProperties.SetName(repositoryLink, row is null ? "View on GitHub" : "View " + row.Name + " on GitHub");
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
        try { snapshot = await online.Fetch(cancellation.Token); catalogStatus.Text = snapshot.Offline ? "Offline · saved catalog from " + snapshot.FetchedUtc.ToLocalTime().ToString("g") : "Catalog up to date · " + snapshot.Document.Mods.Length + " mods"; }
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
        var dialog = new Window { Owner = this, Title = "Preview change", Width = 540, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(26) }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Muted(location ?? target.Text, new Thickness(0, 14, 0, 14)));
        panel.Children.Add(new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap, LineHeight = 23 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) }; panel.Children.Add(buttons);
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
            if (plan.ChangedFiles.Length > 0 && inspection?.Running is true) { message.Text = "Close this Designer installation, then refresh to apply the change."; return; }
            string caption = action switch { "Adopt" => "Manage existing mod", "Undo" => "Undo last change", "Recover" => "Recover interrupted change", _ => action + " " + row?.Name };
            string detail = action == "Adopt" ? "Start tracking this installed version. Its files will stay the same." : $"{plan.ChangedFiles.Length} files will change. Previous files are kept for undo.";
            if (action is "Install" or "Adopt") detail = "Selected version: " + choice?.Version + "\nInstalled version: " + row?.Installed + "\n\n" + detail;
            if (!Preview(caption, detail)) return;
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
        catch (Exception error) { message.Text = error.GetBaseException().Message; }
        finally { busy = applying = false; Buttons(); }
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
        catch (Exception error) { message.Text = error.GetBaseException().Message; }
    }
    internal void SmokeSetup(string appearance, CatalogDocument? document)
    {
        smoke = true; themes.Select(Enum.Parse<Appearance>(appearance), false);
        theme.SelectedItem = theme.Items.Cast<ThemeChoice>().Single(t => t.Value == themes.Settings.Theme);
        if (document is not null) { snapshot = new(DateTimeOffset.UtcNow, OnlineCatalog.Validate(document)); catalogStatus.Text = "Catalog up to date · " + document.Mods.Length + " mods"; }
    }
    internal object SmokeResult() => new { compatible = inspection is not null, rows = mods.Items.Count, packages = packages.Count, running = inspection?.Running, theme = themes.Settings.Theme.ToString(), dark = themes.Dark, mods = mods.Items.Cast<ModRow>().ToArray(), message = message.Text };
    internal Task SmokeFetch() => FetchCatalog();
    internal string[] SmokeLayoutChecks(bool minimum)
    {
        var result = new List<string>(); UpdateLayout();
        void Check(bool value, string label) { if (!value) throw new Exception(label); result.Add(label); }
        Rect Bounds(FrameworkElement item) => new(item.TranslatePoint(new Point(), this), item.RenderSize);
        Check(Math.Abs(Bounds(browse).Left - Bounds(target).Right - 8) < 1, "Installation selector has an 8-unit gap before Browse");
        Check(Math.Abs(target.ActualHeight - browse.ActualHeight) < 1 && Math.Abs(target.ActualHeight - 36) < 1, "Installation controls share a 36-unit height");
        Check(Math.Abs(search.ActualHeight - scope.ActualHeight) < 1, "Search and list selector share a height");
        Check(Bounds(search).Right <= Bounds(compatibleOnly).Left - 8, "Search and compatibility filter do not overlap");
        var placeholder = (TextBlock)search.Template.FindName("Placeholder", search);
        search.Text = ""; UpdateLayout();
        Point hint = placeholder.TranslatePoint(new Point(), search);
        Check(Math.Abs(hint.Y + placeholder.ActualHeight / 2 - search.ActualHeight / 2) < 1, "Search placeholder is vertically centered");
        search.Text = "snapshot"; UpdateLayout(); Rect caret = search.GetRectFromCharacterIndex(0);
        Check(!caret.IsEmpty && Math.Abs(caret.Top + caret.Height / 2 - search.ActualHeight / 2) < 1, "Typed search text is vertically centered");
        Check(Math.Abs(caret.Left - hint.X) < 1, "Typed text and placeholder share their left inset");
        search.Text = ""; UpdateLayout();
        if (!minimum) Check(detailScroll.ScrollableHeight < 1, "All mod detail actions fit at the default window size");
        else { detailScroll.ScrollToBottom(); UpdateLayout(); Check(Bounds(repository).Bottom <= Bounds(detailScroll).Bottom + 1, "Scrolling reveals the final detail action at minimum size"); detailScroll.ScrollToTop(); UpdateLayout(); }
        Check(System.Windows.Automation.AutomationProperties.GetName(search) == "Search mods", "Search has a persistent accessible name");
        Check(Math.Abs(repositoryIcon.ActualWidth - 16) < 1 && Math.Abs(repositoryIcon.ActualHeight - 16) < 1 && !repositoryIcon.Data.IsEmpty(), "Official GitHub vector renders at 16 units");
        Check(System.Windows.Media.VisualTreeHelper.GetDescendantBounds((System.Windows.Media.Visual)Content).Width <= ((FrameworkElement)Content).ActualWidth + 1, "Content fits the window width");
        return result.ToArray();
    }
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
    internal string[] SmokeChecks()
    {
        var result = new List<string>();
        void Check(bool value, string label) { if (!value) throw new Exception(label); result.Add(label); }
        var linkPeer = new System.Windows.Automation.Peers.HyperlinkAutomationPeer(repositoryLink);
        Check(linkPeer.GetAutomationControlType() == System.Windows.Automation.Peers.AutomationControlType.Hyperlink && linkPeer.GetName().Contains("Schematic Tools"), "GitHub navigation exposes a named hyperlink");
        mods.SelectedIndex = 1; Check(repositoryLink.NavigateUri?.AbsoluteUri == "https://github.com/joshua-small/qsys-automatic-geometry", "GitHub destination follows the selected mod"); mods.SelectedIndex = 0;
        Check(!install.IsEnabled && (string)install.Content == "Installed" && install.ToolTip.ToString()!.Contains("already installed"), "Installed state explains why installation is unavailable");
        Check(ToolTipService.GetShowOnDisabled(adopt) && adopt.ToolTip.ToString()!.Contains("already managed"), "Disabled management action explains its state");
        Appearance previous = themes.Settings.Theme;
        theme.SelectedItem = theme.Items.Cast<ThemeChoice>().Single(t => t.Value == Appearance.Dark); Check(themes.Dark, "Theme selector switches to Dark");
        theme.SelectedItem = theme.Items.Cast<ThemeChoice>().Single(t => t.Value == Appearance.Light); Check(!themes.Dark, "Theme selector switches to Light");
        theme.SelectedItem = theme.Items.Cast<ThemeChoice>().Single(t => t.Value == previous);
        search.Text = "snapshot"; Check(mods.Items.Count == 1 && ((ModRow)mods.Items[0]).Id == "schematic-tools", "Typing filters the actual mod list");
        search.Text = "no matching mod fixture"; Check(mods.Items.Count == 0, "Empty search result is handled");
        search.Text = ""; Check(mods.Items.Count == 2, "Clearing search restores both mods");
        scope.SelectedIndex = 1; Check(mods.Items.Count == 2, "Installed filter includes both managed mods"); scope.SelectedIndex = 0;
        versions.IsDropDownOpen = true; UpdateLayout(); Check(versions.IsDropDownOpen, "Version dropdown opens"); versions.IsDropDownOpen = false;
        var future = new ManagerRelease("99.0.0", "v99.0.0", "https://github.com/joshua-small/qsys-mod-manager/releases/download/v99.0.0/QDS-Mod-Manager-99.0.0-win-x64.zip", new string('0', 64), 1);
        ShowManagerRelease(future); Buttons(); Check(installUpdate.IsVisible && installUpdate.IsEnabled, "New manager release enables install button even while QDS is running");
        ShowManagerRelease(future with { Version = AppUpdate.CurrentVersion }); Buttons(); Check(installUpdate.Visibility == Visibility.Collapsed, "Current manager version hides update action");
        updateStatus.Text = AppIdentity.Name + " " + AppUpdate.CurrentVersion;
        return result.ToArray();
    }
}
