using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace QdsModManager.App;

internal sealed class TaskbarUpdates : IDisposable
{
    private readonly MainWindow window;
    private readonly Action<IReadOnlyList<JumpTask>> publish;
    private readonly TaskbarItemInfo info = new();
    private readonly ImageSource badge = UpdateBadge.Image();
    private string? published;
    internal TaskbarUpdates(MainWindow window, Action<IReadOnlyList<JumpTask>> publish)
    {
        this.window = window; this.publish = publish;
        window.TaskbarItemInfo = info; window.UpdateStateChanged += Refresh;
        Refresh();
    }
    internal void Refresh()
    {
        var state = window.UpdateState;
        info.Overlay = state.HasUpdates ? badge : null;
        info.Description = state.HasUpdates ? AppIdentity.Name + " · " + state.Description : AppIdentity.Name;
        var tasks = Tasks(state, Environment.ProcessPath!);
        string key = string.Join("\n", tasks.Select(task => task.Title + task.Arguments));
        if (key == published) return;
        try { publish(tasks); published = key; }
        catch (Exception error) when (error is InvalidOperationException or COMException)
        { window.ReportShellError(error); }
    }
    internal static JumpTask[] Tasks(UpdateStatus state, string executable)
    {
        var tasks = new List<JumpTask>();
        if (state.ManagerVersion is not null && state.CanUpdateManager) Add(state.ManagerLabel, UpdateCommand.Manager, "Review and install the Manager update.");
        if (state.ModCount > 0 && state.CanUpdateMods) Add(UpdateStatus.ModLabel(state.ModCount), UpdateCommand.Mods, "Review updates for the selected Designer installation.");
        return tasks.ToArray();
        void Add(string title, UpdateCommand command, string description) => tasks.Add(new JumpTask
        {
            Title = title, Description = description, Arguments = UpdateStatus.Argument(command),
            ApplicationPath = executable, WorkingDirectory = Path.GetDirectoryName(executable), IconResourcePath = executable, IconResourceIndex = 0
        });
    }
    internal static void Publish(IReadOnlyList<JumpTask> tasks)
    {
        // A development DLL host must not register tasks pointing at dotnet.exe.
        if (!AppIdentity.IsExecutable(Environment.ProcessPath, AppContext.BaseDirectory)) return;
        var list = new JumpList(tasks, false, false);
        JumpList.SetJumpList(Application.Current, list); list.Apply();
    }
    public void Dispose()
    {
        window.UpdateStateChanged -= Refresh; info.Overlay = null;
        try { publish([]); }
        catch (Exception error) when (error is InvalidOperationException or COMException) { window.ReportShellError(error); }
    }
}

internal static class UpdateBadge
{
    internal static ImageSource Image()
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0, 115, 230)), new Pen(Brushes.White, 1), new Point(8, 8), 7.5, 7.5);
            context.PushTransform(new TranslateTransform(3, 3)); context.PushTransform(new ScaleTransform(.625, .625));
            context.DrawGeometry(Brushes.White, null, (Geometry)Application.Current.FindResource("FluentArrowDownload"));
            context.Pop(); context.Pop();
        }
        var image = new DrawingImage(drawing); image.Freeze(); return image;
    }
    internal static System.Drawing.Icon TrayIcon(ImageSource original)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        { context.DrawImage(original, new Rect(0, 0, 32, 32)); context.DrawImage(Image(), new Rect(16, 16, 16, 16)); }
        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
        using var raster = new System.Drawing.Bitmap(stream); var handle = raster.GetHicon();
        try { using var borrowed = System.Drawing.Icon.FromHandle(handle); return (System.Drawing.Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
}
