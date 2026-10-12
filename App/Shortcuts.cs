using System.IO;
using System.Runtime.InteropServices;

namespace QdsModManager.App;

internal static class Shortcuts
{
    internal static void Refresh()
    {
        // Only repair this installation's exact existing shortcuts. Preserve
        // a user-selected icon and never overwrite an unrelated shortcut.
        string root = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        string executable = AppIdentity.ExecutableAt(root);
        string[] folders = { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar") };
        var type = Type.GetTypeFromProgID("WScript.Shell"); if (type is null) return;
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            foreach (string folder in folders) foreach (string stem in AppIdentity.Stems)
            {
                string source = Path.Combine(folder, stem + ".lnk"); if (!File.Exists(source)) continue;
                SelfUpdater.Guard(source); dynamic link = shell.CreateShortcut(source);
                try
                {
                    if (!AppIdentity.IsExecutable((string)link.TargetPath, root)) continue;
                    link.TargetPath = executable; link.WorkingDirectory = root; link.Description = AppIdentity.Name;
                    string icon = (string)link.IconLocation;
                    if (string.IsNullOrWhiteSpace(icon) || icon == ",0" || AppIdentity.Stems.Any(s => icon.StartsWith(Path.Combine(root, s + ".exe"), StringComparison.OrdinalIgnoreCase))) link.IconLocation = executable + ",0";
                    link.Save();
                }
                finally { Marshal.FinalReleaseComObject(link); }
                string renamed = Path.Combine(folder, AppIdentity.Name + ".lnk");
                if (source != renamed && !File.Exists(renamed)) File.Move(source, renamed);
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
}
