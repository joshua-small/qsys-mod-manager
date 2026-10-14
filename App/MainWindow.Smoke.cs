using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace QdsModManager.App;

internal sealed partial class MainWindow
{
    internal string[] SmokeChecks()
    {
        var checks = new List<string>(); void Check(bool pass, string label) { if (!pass) throw new Exception(label); checks.Add(label); }
        Check(detailCard.Visibility == Visibility.Collapsed && listing.Visibility == Visibility.Visible, "List starts full width with details closed");
        Check(mods.Items.Count == 2, "Both supported mods are listed"); mods.SelectedIndex = 0; UpdateLayout();
        Check(detailCard.Visibility == Visibility.Visible && repositoryLink.NavigateUri is not null, "Selecting a row reveals details and repository");
        Check(AutomationProperties.GetName(repositoryLink).Contains(((ModRow)mods.SelectedItem).Name), "Repository accessible name follows selected mod");
        Check(authorLink.NavigateUri?.AbsoluteUri == "https://github.com/joshua-small" && AutomationProperties.GetName(authorLink) == "joshua-small on GitHub", "Author username links to its GitHub profile");
        Check(notice.Visibility == Visibility.Collapsed, "Idle browsing has no footer or status notice");
        message.Text = "An operation needs attention."; Check(notice.Visibility == Visibility.Visible, "Operation feedback is visible without a footer"); message.Text = "";
        Check(applyProfile.Visibility == Visibility.Collapsed && profileState.Visibility == Visibility.Collapsed, "Applied profile does not repeat its name or show an unnecessary apply action");
        var savedProfiles = profiles.ItemsSource; var savedSelection = profiles.SelectedItem; var alternative = ModProfile.Create("Unapplied profile");
        profiles.ItemsSource = profiles.Items.Cast<ModProfile>().Append(alternative).ToArray(); profiles.SelectedItem = alternative;
        Check(applyProfile.Visibility == Visibility.Visible && profileState.Text == "Selection not applied", "Different profile selection exposes preview and unapplied state");
        profiles.ItemsSource = savedProfiles; profiles.SelectedItem = savedSelection;
        Check(AutomationProperties.GetName(profiles) == "Profile" && AutomationProperties.GetName(search) == "Search mods", "Profile and search retain accessible names");
        var originalTheme = themes.Settings.Theme;
        foreach (Appearance appearance in Enum.GetValues<Appearance>()) { themes.Select(appearance, false); Check(themes.Settings.Theme == appearance, "Theme renders: " + appearance); }
        themes.Select(originalTheme, false);
        theme.SelectedItem = theme.Items.Cast<ThemeChoice>().First(t => t.Value == themes.Settings.Theme);
        search.Text = "no matching mod fixture"; Check(mods.Items.Count == 0 && emptyList.Visibility == Visibility.Visible, "Empty search explains reset action"); search.Clear();
        scope.SelectedIndex = 1; Check(mods.Items.Count == 2, "Compatible filter includes supported mods");
        scope.SelectedIndex = 2; Check(mods.Items.Count == inspection!.Mods.Count(m => m.Managed || m.Enabled), "Installed filter reflects installation");
        scope.SelectedIndex = 3; Check(mods.Items.Count == inspection.Mods.Count(m => m.Managed && m.Enabled), "Enabled filter reflects installation");
        scope.SelectedIndex = 4; Check(mods.Items.Count == inspection.Mods.Count(m => m.Managed && !m.Enabled), "Disabled filter reflects installation");
        scope.SelectedIndex = 5; Check(mods.Items.Count == AvailableUpdates().Length, "Updates filter agrees with update review"); scope.SelectedIndex = 0;
        Check(profiles.Items.Count > 0 && inspection.Profile is not null, "Existing configuration is represented as an initial profile");
        Check(migrate.Visibility == (inspection.Profile!.Mods.Any(p => p.Id == "schematic-tools" && Version.Parse(p.Version) < new Version(0,17,0)) ? Visibility.Visible : Visibility.Collapsed), "Split migration action appears only for a managed legacy bundle");
        var savedInspection = inspection; busy = applying = true; Buttons(); Check(!launch.IsEnabled && !applyProfile.IsEnabled && !suspend.IsEnabled && !manageProfile.IsEnabled && notice.Visibility == Visibility.Visible, "Launch and profile changes blocked with visible progress while applying"); busy = applying = false;
        inspection = inspection with { Pending = "smoke-pending" }; Buttons(); Check(!launch.IsEnabled && undo.IsEnabled && (string)undo.Content == "Recover interrupted change" && notice.Visibility == Visibility.Visible && recoverNow.Visibility == Visibility.Visible, "Inline recovery stays visible and blocks launch"); inspection = savedInspection; Buttons();
        mods.SelectedItem = null; UpdateLayout();
        return checks.ToArray();
    }
    internal string[] SmokeLayoutChecks(bool minimum)
    {
        var checks = new List<string>(); void Check(bool pass, string label) { if (!pass) throw new Exception(label); checks.Add(label); }
        UpdateLayout(); Rect Bounds(FrameworkElement item) => item.TransformToAncestor(this).TransformBounds(new Rect(new Point(), item.RenderSize));
        Check(Bounds(launch).Bottom <= ActualHeight && launch.ActualHeight >= 32, "Launch remains anchored and reachable");
        Check(Bounds(search).Right <= Bounds(panes).Right + 1 && search.ActualWidth >= 40, "Search fits remaining width");
        Check(Bounds(sidebarScroll).Right < Bounds(panes).Left, "Sidebar and browsing panes do not overlap");
        mods.SelectedIndex = 0; UpdateLayout();
        Check(detailCard.IsVisible && detailScroll.ViewportHeight > 0 && detailCard.ActualWidth >= 300, "Details retain a usable independent viewport");
        if (ActualWidth < 1060) Check(listing.Visibility == Visibility.Collapsed, "Narrow window gives details the browsing area");
        detailScroll.ScrollToBottom(); UpdateLayout(); Check(Bounds(repository).Bottom <= Bounds(detailScroll).Bottom + 2, "Final details link is reachable by scrolling"); detailScroll.ScrollToTop();
        mods.SelectedItem = null; UpdateLayout(); Check(listing.Visibility == Visibility.Visible && panes.ColumnDefinitions[1].ActualWidth == 0, "Closing details restores list width");
        var savedInspection = inspection; string savedMessage = message.Text;
        inspection = inspection! with { Pending = "layout-recovery" }; message.Text = "An interrupted change needs recovery before you can continue."; Buttons(); UpdateLayout();
        Check(Bounds(notice).Right <= Bounds((FrameworkElement)Content).Right + 1 && Bounds(recoverNow).Right <= Bounds(notice).Right, "Inline recovery fits the available window width");
        inspection = savedInspection; message.Text = savedMessage; Buttons(); UpdateLayout();
        var contentBounds = VisualTreeHelper.GetDescendantBounds((Visual)Content);
        Check(contentBounds.Width <= ((FrameworkElement)Content).ActualWidth + 2, "Content fits window width: " + contentBounds.Width + " / " + ((FrameworkElement)Content).ActualWidth);
        return checks.ToArray();
    }
    internal void SmokeDetails(bool show) { mods.SelectedIndex = show ? 0 : -1; UpdateLayout(); }
    internal void SmokeSelectMod(string id) { mods.SelectedItem = mods.Items.Cast<ModRow>().First(r => r.Id == id); UpdateLayout(); }
    internal Action SmokeLongNames()
    {
        var originalRows = mods.ItemsSource; var originalProfiles = profiles.ItemsSource;
        profiles.ItemsSource = new[] { ModProfile.Create(new string('P', 100)) }; profiles.SelectedIndex = 0;
        mods.ItemsSource = mods.Items.Cast<ModRow>().Select(row => row with { Name = row.Name + " " + new string('M', 160), Remote = row.Remote is null ? null : row.Remote with { Description = string.Concat(Enumerable.Repeat("Long description fixture. ", 100)) } }).ToArray();
        UpdateLayout(); return () => { profiles.ItemsSource = originalProfiles; mods.ItemsSource = originalRows; RefreshProfiles(); mods.SelectedItem = null; };
    }
    internal void SmokeScale(double scale) { ((FrameworkElement)Content).LayoutTransform = new ScaleTransform(scale, scale); UpdateLayout(); }
}
