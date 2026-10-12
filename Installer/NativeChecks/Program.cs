using System.IO;
using System.Reflection;
using Microsoft.Win32;

// Runs only against the unique folder created by Test.ps1, never the user's app.
string target = Path.GetFullPath(args.Single());
if (!Path.GetFileName(target).StartsWith("QMM Installer Validation-", StringComparison.Ordinal)) throw new InvalidOperationException("Expected installer test fixture.");
var assembly = Assembly.LoadFrom(Path.Combine(target, "Q-SYS Mod Manager.dll"));
var registration = assembly.GetType("QdsModManager.App.InstallerRegistration", true)!;
string path = (string)registration.GetField("Key", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
using var key = machine.OpenSubKey(path, writable: true)!;
if (Path.TrimEndingDirectorySeparator((string)key.GetValue("InstallLocation")!) != target) throw new InvalidOperationException("Wrong registered target.");
string original = (string)key.GetValue("DisplayVersion")!;
var refresh = registration.GetMethod("Refresh", BindingFlags.Static | BindingFlags.NonPublic)!;
try
{
    refresh.Invoke(null, [target + "-portable", "99.0.0"]);
    if ((string)key.GetValue("DisplayVersion")! != original) throw new Exception("Portable copy changed installer registration.");
    refresh.Invoke(null, [target, "99.0.0"]);
    if ((string)key.GetValue("DisplayVersion")! != "99.0.0") throw new Exception("In-app update did not refresh Windows version.");
}
finally { key.SetValue("DisplayVersion", original); }
Console.WriteLine("Native installer registration checks passed.");
