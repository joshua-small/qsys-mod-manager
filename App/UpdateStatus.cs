namespace QdsModManager.App;

internal enum UpdateCommand { Open, Manager, Mods }

internal sealed record UpdateStatus(string? ManagerVersion, int ModCount, bool CanUpdateManager, bool CanUpdateMods)
{
    internal bool HasUpdates => ManagerVersion is not null || ModCount > 0;
    internal string ManagerLabel => "Install Manager " + ManagerVersion;
    internal static string ModLabel(int count) => "Install " + count + (count == 1 ? " mod update" : " mod updates");
    internal string Description => string.Join(" · ", new[] {
        ManagerVersion is null ? null : "Manager " + ManagerVersion + " available",
        ModCount > 0 ? ModCount + (ModCount == 1 ? " mod update" : " mod updates") : null
    }.Where(text => text is not null));
    internal static string Argument(UpdateCommand command) => command switch
    {
        UpdateCommand.Manager => "--install-manager-update",
        UpdateCommand.Mods => "--install-mod-updates",
        _ => ""
    };
    internal static UpdateCommand? Parse(string[] args) => args.Length == 0 ? UpdateCommand.Open : args.Length == 1 ? args[0] switch
    {
        "--install-manager-update" => UpdateCommand.Manager,
        "--install-mod-updates" => UpdateCommand.Mods,
        _ => null
    } : null;
}
