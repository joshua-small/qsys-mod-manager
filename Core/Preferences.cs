namespace QdsModManager;

public enum Appearance { System, Light, Dark }
public sealed record Preferences(Appearance Theme = Appearance.System, bool CloseToTray = false,
    bool BackgroundUpdates = true, string? DesignerPath = null)
{
    public static Preferences Load(string path)
    {
        try
        {
            var settings = Data.Read<Preferences>(File.ReadAllBytes(path));
            return Enum.IsDefined(settings.Theme) ? settings : new();
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(string path)
    {
        if (!Enum.IsDefined(Theme)) throw new InvalidDataException("Unknown theme.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string staging = path + "." + Guid.NewGuid().ToString("N");
        try { File.WriteAllBytes(staging, Data.Bytes(this)); File.Move(staging, path, true); }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
    public bool IsDark(bool systemDark) => Theme == Appearance.Dark || Theme == Appearance.System && systemDark;
}
