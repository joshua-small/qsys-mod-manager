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
    // Owner-approved state palette; keep these exact values rather than rounding a percentage mix.
    private const string ActiveBlue = "#0073E6", HoverBlue = "#0059B3", PressedBlue = "#003F80";
    private readonly Window owner;
    private readonly string path;
    internal Preferences Settings { get; private set; }
    internal bool Dark { get; private set; }
    internal Themes(Window owner, string path)
    {
        this.owner = owner; this.path = path; Settings = Preferences.Load(path);
        Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Styles));
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Assets/Icons.xaml") });
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
            ? new[] { "#181818", "#242424", "#1E1E1E", "#F5F5F5", "#BDBDBD", "#757575", "#383838", "#FFB4AB", "#303030", "#999999", "#383838", "#2C2C2C" }
            : new[] { "#F5F5F5", "#FFFFFF", "#FFFFFF", "#202020", "#606060", "#8A8A8A", "#D9D9D9", "#B3261E", "#EBEBEB", "#727272", "#E5E5E5", "#E8E8E8" };
        var names = new[] { "Page", "Card", "Input", "Text", "Muted", "Line", "Hover", "Bad", "Disabled", "DisabledText", "Divider", "ControlSurface" };
        for (int i = 0; i < names.Length; i++) Application.Current.Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        var blue = (Color)ColorConverter.ConvertFromString(ActiveBlue);
        var hover = (Color)ColorConverter.ConvertFromString(HoverBlue);
        var pressed = (Color)ColorConverter.ConvertFromString(PressedBlue);
        var card = ((SolidColorBrush)Application.Current.Resources["Card"]).Color;
        void Set(string key, Color color) => Application.Current.Resources[key] = new SolidColorBrush(color);
        void Surface(string key, Color color) { Set(key, color); Set(key + "Text", Colors.White); }
        Surface("Accent", blue);
        Set("Selected", blue);
        Surface("AccentHover", hover);
        Surface("AccentPressed", pressed);
        Surface("OptionHighlight", hover);
        Set("SelectedRow", Mix(card, blue, Dark ? .20 : .09));
        Set("Link", Mix(blue, Dark ? Colors.White : Colors.Black, Dark ? .50 : .22));
        TitleBar();
    }
    private static Color Mix(Color from, Color to, double amount)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount, MidpointRounding.AwayFromZero);
        return Color.FromRgb(Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
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
      <Style x:Key="KeyboardFocus"><Setter Property="Control.Template"><Setter.Value><ControlTemplate><Border Margin="-3" BorderBrush="{DynamicResource Accent}" BorderThickness="2" CornerRadius="6"/></ControlTemplate></Setter.Value></Setter></Style>
      <DataTemplate x:Key="ActionButtonContent">
        <Grid VerticalAlignment="Center">
          <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
          <Canvas Width="16" Height="16" Margin="0,0,8,0" VerticalAlignment="Center" IsHitTestVisible="False">
            <Path Data="{DynamicResource ActionIcon}" Fill="{Binding Foreground,RelativeSource={RelativeSource AncestorType=Button}}"/>
          </Canvas>
          <TextBlock Grid.Column="1" Text="{Binding}" VerticalAlignment="Center" TextWrapping="Wrap"/>
        </Grid>
      </DataTemplate>
      <Style TargetType="Window">
        <Setter Property="Background" Value="{DynamicResource Page}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="FontFamily" Value="Segoe UI"/><Setter Property="FontSize" Value="14"/>
        <Setter Property="UseLayoutRounding" Value="True"/><Setter Property="SnapsToDevicePixels" Value="True"/>
      </Style>
      <Style TargetType="Button">
        <Setter Property="ToolTipService.ShowOnDisabled" Value="True"/>
        <Setter Property="Background" Value="{DynamicResource Input}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/>
        <Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="12,6"/><Setter Property="MinHeight" Value="36"/><Setter Property="Margin" Value="0,0,8,8"/><Setter Property="FocusVisualStyle" Value="{StaticResource KeyboardFocus}"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
          <Border x:Name="Surface" CornerRadius="4" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="True"/></Border>
          <ControlTemplate.Triggers>
            <Trigger Property="Tag" Value="Subtle"><Setter TargetName="Surface" Property="Background" Value="Transparent"/><Setter TargetName="Surface" Property="BorderBrush" Value="Transparent"/></Trigger>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="{DynamicResource Hover}"/><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="Surface" Property="Background" Value="{DynamicResource Divider}"/></Trigger>
            <Trigger Property="Tag" Value="Primary"><Setter TargetName="Surface" Property="Background" Value="{DynamicResource Accent}"/><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Accent}"/><Setter Property="Foreground" Value="{DynamicResource AccentText}"/></Trigger>
            <MultiTrigger><MultiTrigger.Conditions><Condition Property="Tag" Value="Primary"/><Condition Property="IsMouseOver" Value="True"/></MultiTrigger.Conditions><Setter TargetName="Surface" Property="Background" Value="{DynamicResource AccentHover}"/><Setter Property="Foreground" Value="{DynamicResource AccentHoverText}"/></MultiTrigger>
            <MultiTrigger><MultiTrigger.Conditions><Condition Property="Tag" Value="Primary"/><Condition Property="IsPressed" Value="True"/></MultiTrigger.Conditions><Setter TargetName="Surface" Property="Background" Value="{DynamicResource AccentPressed}"/><Setter Property="Foreground" Value="{DynamicResource AccentPressedText}"/></MultiTrigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Background" Value="{DynamicResource Disabled}"/><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Divider}"/><Setter TargetName="Surface" Property="Opacity" Value="1"/><Setter Property="Foreground" Value="{DynamicResource DisabledText}"/></Trigger>
            <MultiTrigger><MultiTrigger.Conditions><Condition Property="Tag" Value="Subtle"/><Condition Property="IsEnabled" Value="False"/></MultiTrigger.Conditions><Setter TargetName="Surface" Property="Background" Value="Transparent"/><Setter TargetName="Surface" Property="BorderBrush" Value="Transparent"/></MultiTrigger>
          </ControlTemplate.Triggers>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="ContextMenu">
        <Setter Property="Background" Value="{DynamicResource Input}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="BorderBrush" Value="{DynamicResource Divider}"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="5"/><Setter Property="MinWidth" Value="190"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ContextMenu"><Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" Padding="{TemplateBinding Padding}" CornerRadius="4"><StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/></Border></ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="MenuItem">
        <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Padding" Value="10,8"/><Setter Property="FocusVisualStyle" Value="{StaticResource KeyboardFocus}"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem"><Border x:Name="Surface" Background="Transparent" Padding="{TemplateBinding Padding}" CornerRadius="3"><ContentPresenter ContentSource="Header" RecognizesAccessKey="True"/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Surface" Property="Background" Value="{DynamicResource SelectedRow}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="{DynamicResource DisabledText}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="Separator"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Separator"><Border Height="1" Margin="6,4" Background="{DynamicResource Divider}"/></ControlTemplate></Setter.Value></Setter></Style>
      <Style TargetType="Hyperlink">
        <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="TextDecorations" Value="{x:Null}"/><Setter Property="Cursor" Value="Hand"/>
        <Style.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Foreground" Value="{DynamicResource Muted}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="{DynamicResource DisabledText}"/></Trigger></Style.Triggers>
      </Style>
      <Style TargetType="TextBox">
        <Setter Property="Background" Value="{DynamicResource Input}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="CaretBrush" Value="{DynamicResource Text}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/>
        <Setter Property="SelectionBrush" Value="{DynamicResource Accent}"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="12,6"/><Setter Property="MinHeight" Value="36"/><Setter Property="VerticalContentAlignment" Value="Center"/><Setter Property="FocusVisualStyle" Value="{StaticResource KeyboardFocus}"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox">
          <Border x:Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="4">
            <Grid>
              <TextBlock x:Name="Placeholder" Text="{TemplateBinding Tag}" Foreground="{DynamicResource Muted}" IsHitTestVisible="False" Visibility="Collapsed" VerticalAlignment="{TemplateBinding VerticalContentAlignment}" Margin="14,0"/>
              <ScrollViewer x:Name="PART_ContentHost" VerticalAlignment="{TemplateBinding VerticalContentAlignment}" Padding="0"/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="Text" Value=""><Setter TargetName="Placeholder" Property="Visibility" Value="Visible"/></Trigger>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Background" Value="{DynamicResource Disabled}"/><Setter Property="Foreground" Value="{DynamicResource DisabledText}"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="ComboBox">
        <Setter Property="Background" Value="{DynamicResource Input}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/><Setter Property="MinHeight" Value="36"/><Setter Property="FocusVisualStyle" Value="{StaticResource KeyboardFocus}"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
          <Grid>
            <Border x:Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="4">
              <ToggleButton Foreground="{TemplateBinding Foreground}" Focusable="False" ClickMode="Press" IsChecked="{Binding IsDropDownOpen,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}">
                <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="Transparent"><Grid>
                  <ContentPresenter Margin="12,6,32,6" Content="{Binding SelectionBoxItem,RelativeSource={RelativeSource AncestorType=ComboBox}}" ContentTemplate="{Binding SelectionBoxItemTemplate,RelativeSource={RelativeSource AncestorType=ComboBox}}" VerticalAlignment="Center"/>
                  <TextBlock Text="&#xE70D;" FontFamily="Segoe Fluent Icons" FontSize="10" Foreground="{DynamicResource Muted}" HorizontalAlignment="Right" Margin="0,0,12,2" VerticalAlignment="Center"/>
                </Grid></Border></ControlTemplate></ToggleButton.Template>
              </ToggleButton>
            </Border>
            <Popup x:Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False">
              <Border Background="{DynamicResource Input}" BorderBrush="{DynamicResource Line}" BorderThickness="1" CornerRadius="4" Padding="4" MinWidth="{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}">
                <ScrollViewer MaxHeight="280"><StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
            <Trigger Property="IsDropDownOpen" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Background" Value="{DynamicResource Disabled}"/><Setter Property="Foreground" Value="{DynamicResource DisabledText}"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="ComboBoxItem">
        <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Padding" Value="8"/><Setter Property="MinHeight" Value="32"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
          <Grid>
            <Border x:Name="Row" Background="Transparent" Padding="{TemplateBinding Padding}" CornerRadius="3">
              <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="20"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <TextBlock x:Name="SelectionMark" Text="&#xE73E;" FontFamily="Segoe Fluent Icons" FontSize="12" Foreground="{TemplateBinding Foreground}" VerticalAlignment="Center" Visibility="Hidden"/>
                <ContentPresenter Grid.Column="1" VerticalAlignment="Center"/>
              </Grid>
            </Border>
            <Border x:Name="FocusFrame" BorderBrush="{TemplateBinding Foreground}" BorderThickness="1" Margin="2" CornerRadius="2" IsHitTestVisible="False" Visibility="Collapsed"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Row" Property="Background" Value="{DynamicResource OptionHighlight}"/><Setter Property="Foreground" Value="{DynamicResource OptionHighlightText}"/></Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Row" Property="Background" Value="{DynamicResource OptionHighlight}"/><Setter Property="Foreground" Value="{DynamicResource OptionHighlightText}"/><Setter TargetName="FocusFrame" Property="Visibility" Value="Visible"/></Trigger>
            <Trigger Property="IsSelected" Value="True"><Setter TargetName="Row" Property="Background" Value="{DynamicResource Selected}"/><Setter Property="Foreground" Value="{DynamicResource AccentText}"/><Setter TargetName="SelectionMark" Property="Visibility" Value="Visible"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Row" Property="Background" Value="{DynamicResource Disabled}"/><Setter Property="Foreground" Value="{DynamicResource DisabledText}"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="DataGrid"><Setter Property="Background" Value="{DynamicResource Card}"/><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="RowBackground" Value="{DynamicResource Card}"/><Setter Property="BorderThickness" Value="0"/><Setter Property="HorizontalGridLinesBrush" Value="{DynamicResource Divider}"/><Setter Property="VerticalGridLinesBrush" Value="{DynamicResource Divider}"/></Style>
      <Style TargetType="DataGridColumnHeader"><Setter Property="Background" Value="{DynamicResource Card}"/><Setter Property="Foreground" Value="{DynamicResource Muted}"/><Setter Property="Padding" Value="12,10"/><Setter Property="BorderThickness" Value="0"/></Style>
      <Style TargetType="DataGridCell">
        <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Padding" Value="12,8"/><Setter Property="FocusVisualStyle" Value="{StaticResource KeyboardFocus}"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="DataGridCell"><Border Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}"><ContentPresenter VerticalAlignment="Center"/></Border></ControlTemplate></Setter.Value></Setter>
        <Style.Triggers><Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="{DynamicResource Selected}"/><Setter Property="Foreground" Value="{DynamicResource AccentText}"/></Trigger></Style.Triggers>
      </Style>
      <Style TargetType="CheckBox">
        <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="VerticalContentAlignment" Value="Center"/><Setter Property="FocusVisualStyle" Value="{StaticResource KeyboardFocus}"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox"><Grid Background="Transparent">
          <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
          <Border x:Name="Box" Width="18" Height="18" CornerRadius="3" Background="{DynamicResource Input}" BorderBrush="{DynamicResource Line}" BorderThickness="1" VerticalAlignment="Center">
            <TextBlock x:Name="Mark" Text="&#xE73E;" FontFamily="Segoe Fluent Icons" FontSize="12" Foreground="{DynamicResource AccentText}" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>
          </Border>
          <ContentPresenter Grid.Column="1" Margin="8,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True"/>
        </Grid><ControlTemplate.Triggers>
          <Trigger Property="IsChecked" Value="True"><Setter TargetName="Box" Property="Background" Value="{DynamicResource Accent}"/><Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource Accent}"/><Setter TargetName="Mark" Property="Visibility" Value="Visible"/></Trigger>
          <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
          <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger>
        </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="ScrollViewer"><Setter Property="Background" Value="Transparent"/></Style>
      <Style TargetType="ScrollBar"><Setter Property="Background" Value="Transparent"/><Setter Property="Width" Value="12"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar"><Track x:Name="PART_Track" Orientation="{TemplateBinding Orientation}" IsDirectionReversed="True"><Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{DynamicResource Line}" CornerRadius="4" Margin="3"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton></Track></ControlTemplate></Setter.Value></Setter><Style.Triggers><Trigger Property="Orientation" Value="Horizontal"><Setter Property="Width" Value="Auto"/><Setter Property="Height" Value="12"/></Trigger></Style.Triggers></Style>
    </ResourceDictionary>
    """;
}
