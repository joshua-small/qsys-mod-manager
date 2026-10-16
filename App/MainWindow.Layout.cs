using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Automation;

namespace QdsModManager.App;

internal sealed partial class MainWindow
{
    private readonly Button refresh = Button("Refresh"), import = Button("Import package…"), backups = Button("Open backups");
    private readonly Button suspend = Button("Mods on"), updates = Button("Mod updates (0)"), launch = Button("Launch Designer");
    private readonly Button migrate = Button("Split Schematic Tools…");
    private readonly ComboBox profiles = new() { DisplayMemberPath = "Name", MinWidth = 0 };
    private readonly Button applyProfile = Button("Preview & apply");
    private readonly TextBlock profileState = Muted("", new Thickness(0, 4, 0, 8));
    private readonly TextBlock unmanagedState = Muted("", new Thickness(0, 4, 0, 8));
    private readonly TextBlock author = Muted("", new Thickness(0, 4, 0, 8)), releaseNotes = Muted("", new Thickness(0, 8, 0, 8));
    private readonly Hyperlink authorLink = new();
    private readonly Button newProfile = Button("New profile"), manageProfile = Button("Manage");
    private readonly ContextMenu profileMenu = new();
    private readonly List<MenuItem> profileCommands = new();
    private readonly Border notice = new() { Visibility = Visibility.Collapsed };
    private readonly Image detailImage = new() { Height = 96, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock emptyList = Muted("No matching mods. Clear search or reset the filter.", new Thickness(20));
    private readonly Grid panes = new();
    private readonly DockPanel listing = new();
    private Border detailCard = null!;
    private ScrollViewer sidebarScroll = null!;
    private readonly List<Button> profileButtons = new();
    private readonly List<Button> utilityButtons = new();
    private readonly ProgressBar progress = new() { IsIndeterminate = true, Width = 84, Height = 4, Margin = new Thickness(8), Visibility = Visibility.Collapsed };
    private readonly Button recoverNow = Button("Recover interrupted change");
    private void BuildLayout()
    {
        FontSize = 13;
        var root = new DockPanel(); root.SetResourceReference(Panel.BackgroundProperty, "Page"); Content = root;
        var status = new StackPanel(); message.Margin = new Thickness(); status.Children.Add(message);
        var statusActions = new WrapPanel(); status.Children.Add(statusActions);
        recoverNow.Visibility = Visibility.Collapsed; recoverNow.Tag = "Primary"; recoverNow.Margin = new Thickness(0, 4, 8, 0); statusActions.Children.Add(recoverNow); recoverNow.Click += async (_, _) => await Change("Recover");
        statusActions.Children.Add(progress); cancel.Margin = new Thickness(0, 4, 0, 0); statusActions.Children.Add(cancel);
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
        notice.Child = status; notice.Padding = new Thickness(12); notice.Margin = new Thickness(12, 12, 12, 0); notice.CornerRadius = new CornerRadius(4); notice.SetResourceReference(BackgroundProperty, "Card");
        var messageDescriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)); EventHandler messageChanged = (_, _) => RefreshNotice(); messageDescriptor.AddValueChanged(message, messageChanged); Closed += (_, _) => messageDescriptor.RemoveValueChanged(message, messageChanged);
        cancel.Visibility = Visibility.Collapsed; cancel.Click += (_, _) => network?.Cancel();
        var sidebar = new DockPanel { Width = 256, Margin = new Thickness(14, 16, 14, 14) }; DockPanel.SetDock(sidebar, Dock.Left);
        var sideFrame = new Border { Child = sidebar }; sideFrame.SetResourceReference(BackgroundProperty, "Page"); DockPanel.SetDock(sideFrame, Dock.Left); root.Children.Add(sideFrame);
        launch.Tag = "Primary"; launch.Margin = new Thickness(0, 12, 0, 0); DockPanel.SetDock(launch, Dock.Bottom); sidebar.Children.Add(launch);
        var side = new StackPanel(); side.SetValue(TextElement.FontSizeProperty, 12.0);
        var compactButton = new Style(typeof(Button), (Style)FindResource(typeof(Button))); compactButton.Setters.Add(new Setter(Control.MinHeightProperty, 32.0)); compactButton.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 5, 10, 5))); compactButton.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0))); compactButton.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ControlSurface"))); compactButton.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left)); compactButton.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6))); side.Resources.Add(typeof(Button), compactButton);
        var compactCombo = new Style(typeof(ComboBox), (Style)FindResource(typeof(ComboBox))); compactCombo.Setters.Add(new Setter(Control.MinHeightProperty, 34.0)); compactCombo.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ControlSurface"))); side.Resources.Add(typeof(ComboBox), compactCombo);
        sidebarScroll = new() { Content = side, Focusable = false, FocusVisualStyle = null, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; sidebar.Children.Add(sidebarScroll);
        side.Children.Add(SectionHeading("Designer installation", 0)); target.Margin = new Thickness(); target.SetBinding(ToolTipProperty, new Binding("SelectedItem") { Source = target }); side.Children.Add(target);
        var targetText = new FrameworkElementFactory(typeof(TextBlock)); targetText.SetBinding(TextBlock.TextProperty, new Binding { Converter = new InstallationLabel() }); targetText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); target.ItemTemplate = new DataTemplate { VisualTree = targetText };
        var targetTools = new UniformGrid { Columns = 2, Margin = new Thickness(0, 6, 0, 0) }; browse.Margin = new Thickness(0, 0, 3, 0); refresh.Margin = new Thickness(3, 0, 0, 0); targetTools.Children.Add(browse); targetTools.Children.Add(refresh); side.Children.Add(targetTools);
        compatibility.FontSize = 11; compatibility.Margin = new Thickness(0, 7, 0, 10); compatibility.TextWrapping = TextWrapping.Wrap; side.Children.Add(compatibility);
        suspend.Margin = new Thickness(0, 8, 0, 0); side.Children.Add(suspend); unmanagedState.FontSize = 11; unmanagedState.Margin = new Thickness(0, 6, 0, 0); side.Children.Add(unmanagedState);
        side.Children.Add(SectionHeading("Profile")); side.Children.Add(profiles); profiles.SetBinding(ToolTipProperty, new Binding("SelectedItem.Name") { Source = profiles });
        var profileTools = new UniformGrid { Columns = 2, Margin = new Thickness(0, 6, 0, 0) }; side.Children.Add(profileTools);
        newProfile.Margin = new Thickness(0, 0, 3, 0); manageProfile.Margin = new Thickness(3, 0, 0, 0); profileTools.Children.Add(newProfile); profileTools.Children.Add(manageProfile); profileButtons.AddRange([newProfile, manageProfile]);
        AutomationProperties.SetName(newProfile, "New profile"); AutomationProperties.SetName(manageProfile, "Manage profile"); Explain(manageProfile, "Duplicate, rename, delete, import or export profiles");
        newProfile.Click += (_, _) => EditProfile("Add"); manageProfile.ContextMenu = profileMenu; profileMenu.Placement = PlacementMode.Bottom; profileMenu.PlacementTarget = manageProfile;
        manageProfile.Click += (_, _) => { profileMenu.IsOpen = true; };
        foreach (var name in new[] { "Duplicate", "Rename", "Delete", "Import", "Export" })
        {
            if (name == "Import") profileMenu.Items.Add(new System.Windows.Controls.Separator());
            var item = new MenuItem { Header = name + " profile…", Tag = name }; AutomationProperties.SetName(item, name + " profile"); item.Click += (_, _) => EditProfile(name); profileCommands.Add(item); profileMenu.Items.Add(item);
        }
        applyProfile.Margin = new Thickness(0, 8, 0, 0); applyProfile.Tag = "Primary"; side.Children.Add(applyProfile); side.Children.Add(profileState);
        side.Children.Add(SectionHeading("Mod updates")); side.Children.Add(updates); refreshCatalog.Content = "Check for updates"; side.Children.Add(refreshCatalog);
        catalogStatus.FontSize = 11; catalogStatus.Margin = new Thickness(0, 2, 0, 0); side.Children.Add(catalogStatus);
        side.Children.Add(SectionHeading("Tools")); side.Children.Add(import); side.Children.Add(undo);
        side.Children.Add(migrate); migrate.Click += async (_, _) => await MigrateSchematicTools();
        var settings = Button("Settings & diagnostics"); SetButtonIcon(settings, "Settings"); settings.Click += (_, _) => SettingsDialog(); utilityButtons.Add(settings); side.Children.Add(settings);
        var help = Button("Help & documentation"); help.Click += (_, _) => Open("https://github.com/joshua-small/qsys-mod-manager#readme"); side.Children.Add(help);
        foreach (var (button, icon) in new[] { (browse, "FolderOpen"), (refresh, "ArrowClockwise"), (refreshCatalog, "ArrowClockwise"), (import, "ArrowImport"), (undo, "ArrowUndo"), (backups, "Archive"), (checkUpdate, "ArrowSync"), (install, "ArrowDownload"), (adopt, "Settings"), (remove, "Delete"), (installUpdate, "ArrowDownload") }) SetButtonIcon(button, icon);
        checkUpdate.Content = "Check manager update"; installUpdate.Visibility = Visibility.Collapsed; updateStatus.Text = AppIdentity.Name + " " + AppUpdate.CurrentVersion;
        var main = new DockPanel(); root.Children.Add(main); DockPanel.SetDock(notice, Dock.Top); main.Children.Add(notice);
        panes.Margin = new Thickness(0, 12, 12, 12); panes.ColumnDefinitions.Add(new()); panes.ColumnDefinitions.Add(new() { Width = new GridLength(0) }); main.Children.Add(panes); panes.Children.Add(listing);
        var filters = new DockPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(filters, Dock.Top); listing.Children.Add(filters);
        foreach (string filter in new[] { "All", "Compatible", "Installed", "Enabled", "Disabled", "Updates" }) scope.Items.Add(filter); scope.SelectedIndex = 0; scope.Width = 116;
        DockPanel.SetDock(scope, Dock.Left); filters.Children.Add(scope);
        var reset = Button("Reset"); reset.Margin = new Thickness(6, 0, 0, 0); reset.Tag = "Subtle"; reset.Click += (_, _) => { search.Clear(); scope.SelectedIndex = 0; }; Explain(reset, "Clear search and show all mods"); DockPanel.SetDock(reset, Dock.Right); filters.Children.Add(reset);
        search.MinWidth = 40; filters.Children.Add(search);
        var listArea = new Grid(); listArea.Children.Add(mods); listArea.Children.Add(emptyList); listing.Children.Add(listArea); emptyList.IsHitTestVisible = false;
        mods.HeadersVisibility = DataGridHeadersVisibility.None; mods.BorderThickness = new Thickness(); mods.CanUserSortColumns = false; mods.CanUserResizeColumns = false;
        mods.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled); mods.ClipToBounds = true;
        var cellStyle = new Style(typeof(DataGridCell), (Style)FindResource(typeof(DataGridCell))); cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); cellStyle.Setters.Add(new Setter(UIElement.ClipToBoundsProperty, true));
        var selectedCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true }; selectedCell.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("SelectedRow"))); selectedCell.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Text"))); cellStyle.Triggers.Add(selectedCell); mods.CellStyle = cellStyle;
        var template = (DataTemplate)XamlReader.Parse("""
          <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
            <Grid Margin="12,8"><Grid.ColumnDefinitions><ColumnDefinition Width="58"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
              <Image Width="46" Height="46" Margin="0,0,12,0" VerticalAlignment="Top" Source="{Binding Thumbnail}" Stretch="Uniform"/>
              <StackPanel Grid.Column="1"><TextBlock Text="{Binding Name}" FontWeight="SemiBold" FontSize="15" TextTrimming="CharacterEllipsis"/>
                <TextBlock Text="{Binding Summary}" Margin="0,3,0,3" Opacity="0.85" TextTrimming="CharacterEllipsis"/>
                <TextBlock Text="{Binding VersionSummary}" FontSize="11" Opacity="0.8" TextTrimming="CharacterEllipsis"/></StackPanel>
            </Grid>
          </DataTemplate>
          """);
        mods.Columns.Add(new DataGridTemplateColumn { CellTemplate = template, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var rowActions = new FrameworkElementFactory(typeof(StackPanel)); rowActions.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        foreach (bool primary in new[] { true, false })
        {
            var button = new FrameworkElementFactory(typeof(Button)); button.SetValue(FrameworkElement.WidthProperty, 32.0); button.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0)); button.SetValue(Control.PaddingProperty, new Thickness(6)); button.SetValue(Control.BorderThicknessProperty, new Thickness(0)); button.SetResourceReference(Control.BackgroundProperty, "ControlSurface");
            button.SetBinding(AutomationProperties.NameProperty, new Binding(primary ? "PrimaryLabel" : "RemoveLabel")); button.SetBinding(FrameworkElement.ToolTipProperty, new Binding(primary ? "PrimaryLabel" : "RemoveLabel")); button.SetBinding(UIElement.IsEnabledProperty, new Binding(primary ? "PrimaryEnabled" : "RemoveEnabled"));
            var icon = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path)); icon.SetValue(FrameworkElement.WidthProperty, 16.0); icon.SetValue(FrameworkElement.HeightProperty, 16.0); icon.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.Uniform); icon.SetResourceReference(System.Windows.Shapes.Path.FillProperty, "Text");
            if (primary) icon.SetBinding(System.Windows.Shapes.Path.DataProperty, new Binding("PrimaryIcon")); else icon.SetValue(System.Windows.Shapes.Path.DataProperty, FindResource("FluentDelete"));
            button.AppendChild(icon); button.SetValue(FrameworkElement.TagProperty, primary ? "primary" : "remove"); button.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(RowAction)); rowActions.AppendChild(button);
        }
        mods.Columns.Add(new DataGridTemplateColumn { CellTemplate = new DataTemplate { VisualTree = rowActions }, Width = new DataGridLength(76) });
        mods.SizeChanged += (_, _) => { if (mods.ActualWidth > 0) mods.Columns[0].Width = new DataGridLength(Math.Max(150, mods.ActualWidth - 92)); };
        var detail = new StackPanel(); detailScroll.Content = detail; detailCard = Card(detailScroll); detailCard.Padding = new Thickness(16); detailCard.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(detailCard, 1); panes.Children.Add(detailCard);
        var close = Button("Close details"); close.Tag = "Subtle"; close.HorizontalAlignment = HorizontalAlignment.Right; close.Click += (_, _) => { mods.SelectedItem = null; mods.Focus(); }; detail.Children.Add(close);
        heading.FontSize = 21; heading.TextWrapping = TextWrapping.Wrap; detail.Children.Add(detailImage); detail.Children.Add(heading); detail.Children.Add(author); detail.Children.Add(description); detail.Children.Add(availability); detail.Children.Add(SectionHeading("Version", 0)); detail.Children.Add(versions);
        author.Inlines.Add(new Run("by ")); author.Inlines.Add(authorLink); authorLink.SetResourceReference(Hyperlink.ForegroundProperty, "Link"); authorLink.TextDecorations = TextDecorations.Underline; authorLink.RequestNavigate += (_, args) => { Open(args.Uri.AbsoluteUri); args.Handled = true; };
        var actions = new WrapPanel(); foreach (var button in new[] { install, adopt, toggle, remove }) { button.Margin = new Thickness(0, 0, 6, 6); actions.Children.Add(button); } detail.Children.Add(actions); install.Tag = "Primary";
        detail.Children.Add(SectionHeading("Release notes", 20)); detail.Children.Add(releaseNotes);
        repositoryIcon.Data = (Geometry)FindResource("GitHubMark"); repositoryIcon.SetBinding(System.Windows.Shapes.Shape.FillProperty, new Binding(nameof(Hyperlink.Foreground)) { Source = repositoryLink });
        repositoryLink.Inlines.Add(new InlineUIContainer(repositoryIcon)); repositoryLink.Inlines.Add(new Run("Source & help on GitHub")); repository.Inlines.Add(repositoryLink); repository.Margin = new Thickness(0, 12, 0, 0); detail.Children.Add(repository);
        foreach (var (control, name) in new (DependencyObject, string)[] { (target, "Designer installation"), (theme, "Theme"), (scope, "Mod filter"), (search, "Search mods"), (versions, "Mod version"), (mods, "Available mods"), (profiles, "Profile") }) AutomationProperties.SetName(control, name);
        profiles.SelectionChanged += (_, _) => RefreshSidebar(); applyProfile.Click += async (_, _) => { if (profiles.SelectedItem is ModProfile profile) await ApplyProfile(profile); };
        suspend.Click += async (_, _) => { if (inspection?.Profile is { } profile) await ApplyProfile(profile, !inspection.Suspended, inspection.Suspended ? "Resume" : "Suspend"); };
        updates.Click += (_, _) => ReviewUpdates(); launch.Click += (_, _) => Launch(); versions.SelectionChanged += (_, _) => ShowPresentation(mods.SelectedItem as ModRow);
        SizeChanged += (_, _) => ArrangeDetails(); panes.SizeChanged += (_, _) => ArrangeDetails(); ArrangeDetails();
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && mods.SelectedItem is not null && !busy) { mods.SelectedItem = null; mods.Focus(); e.Handled = true; } };
    }
    private static TextBlock SectionHeading(string text, double top = 20) => new() { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, top, 0, 10) };
    private void RefreshNotice() => notice.Visibility = busy || inspection?.Pending is not null || !string.IsNullOrWhiteSpace(message.Text) ? Visibility.Visible : Visibility.Collapsed;
    private sealed class InstallationLabel : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is string path ? Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) : "";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
    private static Border Separator() { var b = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) }; b.SetResourceReference(BackgroundProperty, "Divider"); return b; }
    private void ArrangeDetails()
    {
        if (detailCard is null) return; bool selected = mods.SelectedItem is ModRow; bool narrow = panes.ActualWidth > 0 && panes.ActualWidth < 760;
        detailCard.Visibility = selected ? Visibility.Visible : Visibility.Collapsed; listing.Visibility = selected && narrow ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(detailCard, narrow ? 0 : 1); detailCard.Margin = new Thickness(narrow ? 0 : 12, 0, 0, 0);
        panes.ColumnDefinitions[1].Width = selected && !narrow ? new GridLength(0.85, GridUnitType.Star) : new GridLength(0);
    }
    private async void RowAction(object sender, RoutedEventArgs e)
    {
        if (busy || inspection is null || inspection.Pending is not null || sender is not Button { DataContext: ModRow row } button) return;
        if (inspection.Suspended) { message.Text = "Turn managed mods on before changing individual mods."; return; }
        mods.SelectedItem = row; e.Handled = true;
        if ((string)button.Tag == "remove") await Change("Remove");
        else if (row.Local?.Managed is true) await Change(row.Local.Enabled ? "Disable" : "Enable");
        else if (row.Local?.Status == "Installed · not managed yet") await Change("Adopt");
        else await Install();
    }
}
