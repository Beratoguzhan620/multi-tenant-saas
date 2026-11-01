namespace Helpdesk.Core.Tenants;

/// <summary>What a plan allows. <c>null</c> means no limit.</summary>
public sealed record Plan(PlanId Id, string Name, int Seats, int? TicketsPerMonth, long StorageBytes, int RequestsPerMinute, int PriceUsd)
{
    public const long MiB = 1024 * 1024;

    public static readonly Plan Free = new(PlanId.Free, "Free", Seats: 3, TicketsPerMonth: 100, StorageBytes: 100 * MiB, RequestsPerMinute: 120, PriceUsd: 0);

    public static readonly Plan Team = new(PlanId.Team, "Team", Seats: 10, TicketsPerMonth: 2_000, StorageBytes: 5 * 1024 * MiB, RequestsPerMinute: 600, PriceUsd: 49);

    public static readonly Plan Business = new(PlanId.Business, "Business", Seats: 50, TicketsPerMonth: null, StorageBytes: 50 * 1024 * MiB, RequestsPerMinute: 3_000, PriceUsd: 199);

    public static IReadOnlyList<Plan> All { get; } = [Free, Team, Business];

    public static Plan For(PlanId id) => id switch
    {
        PlanId.Free => Free,
        PlanId.Team => Team,
        PlanId.Business => Business,
        _ => throw new DomainException(ErrorCodes.ValidationFailed, "There is no such plan."),
    };

    /// <summary>
    /// What stands in the way of moving to this plan. A downgrade is refused while the organization uses more
    /// than the smaller plan allows; nothing is deleted on its behalf. This month's tickets never block: the new
    /// limit simply applies to the next ticket.
    /// </summary>
    public IReadOnlyList<string> Blockers(int seatsUsed, long storageUsed)
    {
        var blockers = new List<string>();
        if (seatsUsed > Seats)
        {
            blockers.Add($"The {Name} plan has {Seats} seats and {seatsUsed} are in use (members and open invitations). Remove {seatsUsed - Seats} first.");
        }

        if (storageUsed > StorageBytes)
        {
            blockers.Add($"The {Name} plan stores {StorageBytes / MiB} MB and attachments use {Math.Ceiling(storageUsed / (double)MiB)} MB. Delete some attachments first.");
        }

        return blockers;
    }
}
