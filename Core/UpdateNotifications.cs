namespace QdsModManager;

// Session-local deduplication: a failed/offline check must not clear remembered updates.
public sealed class UpdateNotifications
{
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    public string[] TakeNew(IEnumerable<string> updates) => updates.Distinct(StringComparer.Ordinal).Where(seen.Add).ToArray();
}
