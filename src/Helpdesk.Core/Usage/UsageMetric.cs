namespace Helpdesk.Core.Usage;

/// <summary>
/// Running totals per organization, kept in <c>usage_counters</c> and changed with one conditional
/// <c>UPDATE</c> each: the limit check and the increment are the same statement, so two requests at the same
/// moment cannot both take the last ticket or the last megabyte.
/// </summary>
public static class UsageMetric
{
    /// <summary>Tickets opened in a calendar month (UTC); the period is <c>yyyy-MM</c>.</summary>
    public const string Tickets = "tickets";

    /// <summary>Bytes of attachments; one period for all time.</summary>
    public const string StorageBytes = "storage_bytes";

    public const string AllTime = "all";

    public static string Month(DateTimeOffset now) => now.UtcDateTime.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
}
