using System.IO;
using Microsoft.Win32;

namespace QdsModManager.App;

internal static class InstallerRegistration
{
    // Stable Inno Setup AppId; portable copies must never change this record.
    internal const string Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B8AD941B-5D52-4BDA-B744-80586E33E3E1}_is1";

    internal static void Refresh(string target, string version)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var registration = machine.OpenSubKey(Key, writable: true);
        if (registration?.GetValue("InstallLocation") is not string location ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)), StringComparison.OrdinalIgnoreCase)) return;
        registration.SetValue("DisplayVersion", version, RegistryValueKind.String);
    }
}
