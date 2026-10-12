using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace QdsModManager.App;

internal sealed class Themes : IDisposable
{
    private readonly Window owner;
    private readonly string path;
    internal Preferences Settings { get; private set; }
    internal bool Dark { get; private set; }
    internal Themes(Window owner, string path)
    {
        this.owner = owner; this.path = path; Settings = Preferences.Load(path);
        Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Styles));
        Apply(); owner.SourceInitialized += (_, _) => TitleBar();
        SystemEvents.UserPreferenceChanged += SystemChanged;
    }
    internal void Select(Appearance appearance, bool persist = true)
    {
        var next = new Preferences(appearance); if (persist) next.Save(path); Settings = next; Apply();
    }
    private void SystemChanged(object sender, UserPreferenceChangedEventArgs args) => owner.Dispatcher.BeginInvoke(Apply);
    private void Apply()
    {
        bool systemDark = false;
        try { systemDark = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        Dark = Settings.IsDark(systemDark);
        var colors = Dark
            ? new[] { "#191D22", "#242A31", "#1D232A", "#F0F3F6", "#B3BFCA", "#47515D", "#343F4B", "#60C5D4", "#10252A", "#96D7AB", "#FFAF99", "#334C58" }
            : new[] { "#F2F5F7", "#FFFFFF", "#FFFFFF", "#24343C", "#51666F", "#BAC8CE", "#E5EDF1", "#096475", "#FFFFFF", "#286341", "#9C422F", "#D5E9EF" };
        var names = new[] { "Page", "Card", "Input", "Text", "Muted", "Line", "Hover", "Accent", "AccentText", "Good", "Bad", "Selected" };
        for (int i = 0; i < names.Length; i++) Application.Current.Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        TitleBar();
    }
    private void TitleBar()
    {
        var handle = new WindowInteropHelper(owner).Handle; if (handle == IntPtr.Zero) return;
        int dark = Dark ? 1 : 0; _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    public void Dispose() => SystemEvents.UserPreferenceChanged -= SystemChanged;

    private const string Styles = """
    <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
      <Style TargetType="Window"><Setter Property="Background" Value="{DynamicResource Page}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="FontFamily" Value="Segoe UI"/><Setter Property="FontSize" Value="14"/></Style>
      <Style TargetType="Button">
        <Setter Property="Background" Value="{DynamicResource Input}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="14,9"/><Setter Property="Margin" Value="0,0,8,8"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="Surface" CornerRadius="4" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="True"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="TextBox"><Setter Property="Background" Value="{DynamicResource Input}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="CaretBrush" Value="{DynamicResource Text}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/><Setter Property="SelectionBrush" Value="{DynamicResource Selected}"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="10,7"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="4" Padding="{TemplateBinding Padding}"><Grid><TextBlock x:Name="Placeholder" Text="{TemplateBinding Tag}" Foreground="{DynamicResource Muted}" IsHitTestVisible="False" Visibility="Collapsed"/><ScrollViewer x:Name="PART_ContentHost"/></Grid></Border><ControlTemplate.Triggers><Trigger Property="Text" Value=""><Setter TargetName="Placeholder" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
      <Style TargetType="ComboBox"><Setter Property="Background" Value="{DynamicResource Input}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/><Setter Property="MinHeight" Value="36"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid><ToggleButton Foreground="{DynamicResource Text}" Focusable="False" ClickMode="Press" IsChecked="{Binding IsDropDownOpen,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="{DynamicResource Input}" BorderBrush="{DynamicResource Line}" BorderThickness="1" CornerRadius="4"><Grid><ContentPresenter Margin="10,6,28,6" Content="{Binding SelectionBoxItem,RelativeSource={RelativeSource AncestorType=ComboBox}}" ContentTemplate="{Binding SelectionBoxItemTemplate,RelativeSource={RelativeSource AncestorType=ComboBox}}" VerticalAlignment="Center"/><TextBlock Text="⌄" Foreground="{DynamicResource Muted}" HorizontalAlignment="Right" Margin="0,0,10,2" VerticalAlignment="Center"/></Grid></Border></ControlTemplate></ToggleButton.Template></ToggleButton><Popup x:Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False"><Border Background="{DynamicResource Input}" BorderBrush="{DynamicResource Line}" BorderThickness="1" MinWidth="{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}"><ScrollViewer MaxHeight="280"><StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer></Border></Popup></Grid><ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
      <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Padding" Value="10,8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="Row" Background="Transparent" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Row" Property="Background" Value="{DynamicResource Selected}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
      <Style TargetType="DataGrid"><Setter Property="Background" Value="{DynamicResource Card}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="RowBackground" Value="{DynamicResource Card}"/><Setter Property="BorderThickness" Value="0"/><Setter Property="HorizontalGridLinesBrush" Value="{DynamicResource Line}"/><Setter Property="VerticalGridLinesBrush" Value="{DynamicResource Line}"/></Style>
      <Style TargetType="DataGridColumnHeader"><Setter Property="Background" Value="{DynamicResource Card}"/><Setter Property="Foreground" Value="{DynamicResource Muted}"/><Setter Property="Padding" Value="10,12"/><Setter Property="BorderThickness" Value="0"/></Style>
      <Style TargetType="DataGridCell"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Padding" Value="10"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="DataGridCell"><Border Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}"><ContentPresenter VerticalAlignment="Center"/></Border></ControlTemplate></Setter.Value></Setter><Style.Triggers><Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="{DynamicResource Selected}"/></Trigger></Style.Triggers></Style>
      <Style TargetType="CheckBox"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="VerticalContentAlignment" Value="Center"/></Style>
      <Style TargetType="ScrollViewer"><Setter Property="Background" Value="Transparent"/></Style>
      <Style TargetType="ScrollBar"><Setter Property="Background" Value="Transparent"/><Setter Property="Width" Value="12"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar"><Track x:Name="PART_Track" Orientation="{TemplateBinding Orientation}" IsDirectionReversed="True"><Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{DynamicResource Line}" CornerRadius="4" Margin="3"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton></Track></ControlTemplate></Setter.Value></Setter><Style.Triggers><Trigger Property="Orientation" Value="Horizontal"><Setter Property="Width" Value="Auto"/><Setter Property="Height" Value="12"/></Trigger></Style.Triggers></Style>
    </ResourceDictionary>
    """;
}
