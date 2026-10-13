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
        // Setup/uninstall must wait for the UI and its elevated helpers to exit.
        using var installerMutex = new Mutex(false, @"Global\QsysModManager.Application");
        if (args.Length == 2 && args[0].StartsWith("--self-update-", StringComparison.Ordinal))
            return AppUpdate.Run(args[0], args[1]).GetAwaiter().GetResult();
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
        using var uiMutex = new Mutex(false, @"Local\QsysModManager.UserInterface", out bool firstWindow);
        if (args.Length == 0 && !firstWindow) { MessageBox.Show("Q-SYS Mod Manager is already open.", AppIdentity.Name); return 0; }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new MainWindow(args.Length == 5 && args[0] is ("--smoke" or "--preview") ? Path.Combine(Path.GetFullPath(args[2]), "user-data") : null);
        if (args.Length == 5 && args[0] is ("--smoke" or "--preview"))
        {
            window.SmokeSetup(args[3], args[4] == "-" ? null : Data.Read<CatalogDocument>(File.ReadAllBytes(args[4])));
            string output = Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
            if (args[0] == "--preview")
            {
                window.Title += " · isolated preview"; window.Loaded += (_, _) => window.SelectTarget(args[1]); app.Run(window); return 0;
            }
            window.Left = -32000; window.Top = -32000; window.ShowActivated = false; window.ShowInTaskbar = false;
            window.Loaded += async (_, _) =>
            {
                try
                {
                    window.SelectTarget(args[1]); await Task.Delay(350); window.UpdateLayout();
                    if (args[4] == "-") await window.SmokeFetch();
                    File.WriteAllBytes(Path.Combine(output, "checks.json"), Data.Bytes(window.SmokeChecks()));
                    void Capture(string name)
                    {
                        window.UpdateLayout();
                        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
                    }
                    File.WriteAllBytes(Path.Combine(output, "layout.json"), Data.Bytes(window.SmokeLayoutChecks(false)));
                    Capture("manager.png");
                    window.SmokeDetails(true); Capture("manager-details.png"); window.SmokeDetails(false);
                    window.Width = window.MinWidth; window.Height = window.MinHeight; await Task.Delay(150); window.UpdateLayout();
                    File.WriteAllBytes(Path.Combine(output, "layout-minimum.json"), Data.Bytes(window.SmokeLayoutChecks(true)));
                    Capture("manager-minimum.png");
                    window.SmokeDetails(true); Capture("manager-minimum-details.png"); window.SmokeDetails(false);
                    window.Width = 1366; window.Height = 740;
                    foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                    {
                        window.SmokeScale(scale); await Task.Delay(100);
                        File.WriteAllBytes(Path.Combine(output, "layout-scale-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json"), Data.Bytes(window.SmokeLayoutChecks(false)));
                        Capture("manager-scale-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png");
                    }
                    window.SmokeScale(1); var restoreNames = window.SmokeLongNames();
                    File.WriteAllBytes(Path.Combine(output, "layout-long-names.json"), Data.Bytes(window.SmokeLayoutChecks(false)));
                    Capture("manager-long-names.png"); window.SmokeDetails(true); Capture("manager-long-details.png"); restoreNames();
                    File.WriteAllBytes(Path.Combine(output, "ui.json"), Data.Bytes(window.SmokeResult()));
                }
                catch (Exception error) { File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); }
                finally { window.Close(); }
            };
        }
        else if (args.Length != 0) return 2;
        else { try { Shortcuts.Refresh(); } catch { /* A locked shortcut must not prevent startup. */ } }
        app.Run(window); return 0;
    }
    private static void ValidateRequest(string request, string result)
    {
        string folder = Path.Combine(AppIdentity.UserData, "Requests");
        string id = Path.GetFileNameWithoutExtension(request);
        if (!Guid.TryParseExact(id, "N", out _) || !string.Equals(Path.GetFullPath(request), Path.Combine(folder, id + ".json"), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFullPath(result), Path.Combine(folder, id + ".result.json"), StringComparison.OrdinalIgnoreCase) || File.Exists(result)) throw new IOException("Invalid request location.");
        for (string? path = Path.GetFullPath(request); path is not null; path = Path.GetDirectoryName(path))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked request path.");
        try { if ((File.GetAttributes(result) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked result path."); }
        catch (FileNotFoundException) { }
    }
}
