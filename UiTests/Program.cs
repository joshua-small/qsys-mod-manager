using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QdsModManager;

internal static class Program
{
    private static readonly List<string> checks = [];
    [STAThread]
    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args.Single()); Directory.CreateDirectory(output);
        var assembly = Assembly.Load("Q-SYS Mod Manager");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var panel = new StackPanel { Margin = new Thickness(24) };
        var canvas = new Border { Child = panel }; canvas.SetResourceReference(Border.BackgroundProperty, "Page");
        var window = new Window { Width = 480, Height = 420, Content = canvas, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
        var type = assembly.GetType("QdsModManager.App.Themes", true)!;
        using var themes = (IDisposable)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
            [window, Path.Combine(output, Guid.NewGuid().ToString("N"), "preferences.json")], null)!;
        var select = type.GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var primary = new StateButton { Content = "Primary action", Tag = "Primary" };
        var hoverPreview = new Button { Content = "Hover palette preview" };
        hoverPreview.SetResourceReference(Control.BackgroundProperty, "AccentHover"); hoverPreview.SetResourceReference(Control.ForegroundProperty, "AccentHoverText");
        var pressed = new StateButton { Content = "Pressed action", Tag = "Primary" };
        var disabled = new Button { Content = "Disabled action", Tag = "Primary", IsEnabled = false };
        var checkbox = new CheckBox { Content = "Selected control", IsChecked = true, Margin = new Thickness(0, 8, 0, 16) };
        var combo = new ComboBox { MinWidth = 300 };
        var selected = new StateOption { Content = "Committed selection" };
        var highlighted = new StateOption { Content = "Hovered / navigated candidate" };
        var unavailable = new StateOption { Content = "Unavailable option", IsEnabled = false };
        primary.Style = pressed.Style = (Style)app.FindResource(typeof(Button));
        selected.Style = highlighted.Style = unavailable.Style = (Style)app.FindResource(typeof(ComboBoxItem));
        combo.Items.Add(selected); combo.Items.Add(highlighted); combo.Items.Add(unavailable); combo.SelectedIndex = 0;
        panel.Children.Add(primary); panel.Children.Add(hoverPreview); panel.Children.Add(pressed); panel.Children.Add(disabled); panel.Children.Add(checkbox); panel.Children.Add(combo);
        try
        {
            window.Show(); Pump(); pressed.Press(true);
            // Reuse live controls across theme switches, including returning to Light.
            foreach (var appearance in new[] { Appearance.Light, Appearance.Dark, Appearance.System, Appearance.Light })
            {
                select.Invoke(themes, [appearance, false]); Pump();
                string label = appearance.ToString();
                Color accent = Resource("Accent"), hover = Resource("AccentHover"), down = Resource("AccentPressed");
                Check(accent == Color.FromRgb(0, 115, 230), label + ": exact active blue #0073e6");
                Check(hover == Color.FromRgb(0, 89, 179), label + ": exact hover blue #0059b3");
                Check(down == Color.FromRgb(0, 63, 128), label + ": exact pressed blue #003f80");
                Check(accent != hover && hover != down && down != accent, label + ": distinct action shades");
                Check(hover.R == 0 && down.R == 0 && hover.G < accent.G && down.G < hover.G && hover.B < accent.B && down.B < hover.B, label + ": hover and pressed darken the base blue");
                foreach (string key in new[] { "Accent", "AccentHover", "AccentPressed", "OptionHighlight" })
                {
                    Check(Resource(key + "Text") == Colors.White, label + ": " + key + " text stays white");
                    Check(Contrast(Resource(key), Resource(key + "Text")) >= 4.5, label + ": " + key + " text contrast >= 4.5:1");
                }
                Check(Contrast(Resource("Link"), Resource("Card")) >= 4.5, label + ": link contrast >= 4.5:1");
                Check(Background(primary, "Surface") == accent && ColorOf(primary.Foreground) == Resource("AccentText"), label + ": primary action uses base and readable text");
                Check(Background(pressed, "Surface") == down && ColorOf(pressed.Foreground) == Resource("AccentPressedText"), label + ": pressed action uses derived shade and text");
                Check(Background(disabled, "Surface") == Resource("Disabled"), label + ": disabled action remains neutral");
                combo.IsDropDownOpen = true; Pump();
                selected.Highlight(false); highlighted.Highlight(true); Pump();
                Check(combo.SelectedItem == selected, label + ": highlighting does not commit another value");
                Check(Background(selected, "Row") == accent, label + ": selected dropdown item keeps base blue");
                Check(Background(highlighted, "Row") == hover && Resource("OptionHighlight") == hover, label + ": candidate uses the hover shade");
                Check(ColorOf(highlighted.Foreground) == Resource("OptionHighlightText"), label + ": candidate text follows its tint");
                Check(((FrameworkElement)selected.Template.FindName("SelectionMark", selected)).Visibility == Visibility.Visible, label + ": committed selection has a checkmark");
                Check(((FrameworkElement)highlighted.Template.FindName("SelectionMark", highlighted)).Visibility == Visibility.Hidden, label + ": uncommitted candidate has no checkmark");
                selected.Highlight(true); Pump();
                Check(Background(selected, "Row") == accent, label + ": selected color wins when also highlighted");
                unavailable.Highlight(true); Pump();
                Check(Background(unavailable, "Row") == Resource("Disabled") && ColorOf(unavailable.Foreground) == Resource("DisabledText"), label + ": disabled option wins over highlight");
                Capture(canvas, Path.Combine(output, label + "-controls.png"));
                Capture((FrameworkElement)((Popup)combo.Template.FindName("PART_Popup", combo)).Child, Path.Combine(output, label + "-dropdown.png"));
                combo.IsDropDownOpen = false; highlighted.Highlight(false); unavailable.Highlight(false); Pump();
                Check(combo.SelectedItem == selected, label + ": closing the dropdown retains selection");
            }
            File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Passed {checks.Count} native WPF theme/state checks."); return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); Console.Error.WriteLine(error); return 1; }
        finally { window.Close(); app.Shutdown(); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks.Add(message); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static Color Resource(string key) => ColorOf((Brush)Application.Current.Resources[key]);
    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;
    private static Color Background(Control control, string name) { control.ApplyTemplate(); return ColorOf(((Border)control.Template.FindName(name, control)).Background); }
    private static double Contrast(Color a, Color b)
    {
        static double Luminance(Color color)
        {
            double[] channels = [color.R / 255.0, color.G / 255.0, color.B / 255.0];
            var linear = channels.Select(c => c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4)).ToArray();
            return linear[0] * .2126 + linear[1] * .7152 + linear[2] * .0722;
        }
        double first = Luminance(a), second = Luminance(b); return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }
    private static void Capture(FrameworkElement visual, string path)
    {
        visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed class StateOption : ComboBoxItem { internal void Highlight(bool value) => IsHighlighted = value; }
    private sealed class StateButton : Button { internal void Press(bool value) => IsPressed = value; }
}
