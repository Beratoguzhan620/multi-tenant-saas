namespace Helpdesk.Infrastructure.Usage;

/// <summary>One running total: an organization's tickets this month, or the bytes its attachments take.</summary>
public sealed class UsageCounter
{
    public Guid TenantId { get; set; }

    public string Metric { get; set; } = string.Empty;

    public string Period { get; set; } = string.Empty;

    public long Value { get; set; }
}
