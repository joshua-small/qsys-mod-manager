using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QdsModManager;

namespace QdsModManager.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--apply" && args[2] == "--result")
        {
            // An elevated helper only accepts the UI's paired request/receipt
            // names in this user's request directory. Never overwrite a caller-
            // supplied arbitrary result path or follow a linked parent.
            try { ValidateRequest(args[1], args[3]); } catch { return 2; }
            ApplyResult result;
            try
            {
                var plan = Data.Read<OperationPlan>(File.ReadAllBytes(args[1]));
                result = new Manager(plan.Target).Apply(plan);
            }
            catch (Exception error) { result = new(false, error.GetBaseException().Message, null); }
            ValidateRequest(args[1], args[3]);
            using (var receipt = new FileStream(args[3], FileMode.CreateNew, FileAccess.Write, FileShare.None)) { receipt.Write(Data.Bytes(result)); receipt.Flush(true); }
            return result.Success ? 0 : 1;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new MainWindow();
        if (args.Length == 5 && args[0] == "--smoke")
        {
            window.SmokeSetup(args[3], args[4] == "-" ? null : Data.Read<CatalogDocument>(File.ReadAllBytes(args[4])));
            string output = Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
            window.Left = -32000; window.Top = -32000; window.ShowActivated = false; window.ShowInTaskbar = false;
            window.Loaded += async (_, _) =>
            {
                try
                {
                    window.SelectTarget(args[1]); await Task.Delay(350); window.UpdateLayout();
                    if (args[4] == "-") await window.SmokeFetch();
                    File.WriteAllBytes(Path.Combine(output, "checks.json"), Data.Bytes(window.SmokeChecks()));
                    var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(Path.Combine(output, "manager.png"))) encoder.Save(stream);
                    File.WriteAllBytes(Path.Combine(output, "ui.json"), Data.Bytes(window.SmokeResult()));
                }
                catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); }
                finally { window.Close(); }
            };
        }
        else if (args.Length != 0) return 2;
        app.Run(window); return 0;
    }
    private static void ValidateRequest(string request, string result)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QDS Mod Manager", "Requests");
        string id = Path.GetFileNameWithoutExtension(request);
        if (!Guid.TryParseExact(id, "N", out _) || !string.Equals(Path.GetFullPath(request), Path.Combine(folder, id + ".json"), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFullPath(result), Path.Combine(folder, id + ".result.json"), StringComparison.OrdinalIgnoreCase) || File.Exists(result)) throw new IOException("Invalid request location.");
        for (string? path = Path.GetFullPath(request); path is not null; path = Path.GetDirectoryName(path))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked request path.");
        try { if ((File.GetAttributes(result) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked result path."); }
        catch (FileNotFoundException) { }
    }
}
