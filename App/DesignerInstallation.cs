using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace QdsModManager.App;

// Presentation only. Compatibility and consent still use the verified file hashes.
internal sealed record DesignerInstallation(string Label)
{
    internal string Warning => Label + "\n" + DesignerBuild.UntestedWarning;
    internal static DesignerInstallation Read(string directory)
    {
        try
        {
            // Prefer the host used for compatibility checks. Older installations
            // without that DLL can still show their executable's version.
            string host = Path.Combine(directory, "Q-Sys Designer.dll");
            if (!File.Exists(host)) host = Path.Combine(directory, "Q-Sys Designer.exe");
            if (File.Exists(host))
            {
                var version = FileVersionInfo.GetVersionInfo(host);
                if (version.FileMajorPart > 0 && version.FileVersion is not null)
                {
                    string number = $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}";
                    if (version.FilePrivatePart != 0) number += "." + version.FilePrivatePart;
                    return new("Q-SYS Designer " + number);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException) { }
        return new("Designer version unavailable");
    }
}
