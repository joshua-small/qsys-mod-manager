namespace QdsModManager;

public static class AppIdentity
{
    public const string Name = "Q-SYS Mod Manager";
    public const string LegacyName = "QDS Mod Manager";
    public static readonly string[] Stems = { Name, LegacyName };
    // Retain the existing per-user data location across the product rename.
    public static string UserData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyName);
    public static string[] Files(string stem) => new[] { stem + ".exe", stem + ".dll", stem + ".deps.json", stem + ".runtimeconfig.json", "QdsModManager.Core.dll", "README.md", "THIRD-PARTY-NOTICES.txt" };
    public static string StemAt(string directory)
    {
        var found = Stems.Where(s => File.Exists(Path.Combine(directory, s + ".dll"))).ToArray();
        if (found.Length != 1) throw new IOException("The Q-SYS Mod Manager installation is incomplete or contains conflicting app files.");
        return found[0];
    }
    public static string ExecutableAt(string directory) => Path.Combine(directory, StemAt(directory) + ".exe");
    public static bool IsExecutable(string? path, string directory) => Stems.Any(s => string.Equals(path, Path.Combine(directory, s + ".exe"), StringComparison.OrdinalIgnoreCase));
}
