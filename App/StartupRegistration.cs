using System.IO;
using Microsoft.Win32;

namespace QdsModManager.App;

internal sealed class StartupRegistration
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "QsysModManager";
    private readonly string executable, keyPath;
    internal StartupRegistration(string executable, string keyPath = RunKey)
    {
        this.executable = Path.GetFullPath(executable); this.keyPath = keyPath;
    }
    internal string Command => "\"" + executable + "\" --background";
    internal bool Enabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(keyPath); return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase); }
    }
    internal void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, true);
        if (enabled)
        {
            if (!File.Exists(executable)) throw new FileNotFoundException("Open the installed Q-SYS Mod Manager executable before enabling startup.", executable);
            // Windows owns StartupApproved. Never bypass a user's Windows startup disable.
            key.SetValue(ValueName, Command, RegistryValueKind.String);
        }
        else if (Enabled) key.DeleteValue(ValueName, false); // Preserve another portable copy's registration.
    }
}
