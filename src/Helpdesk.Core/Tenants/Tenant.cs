using System.Text.RegularExpressions;

namespace Helpdesk.Core.Tenants;

/// <summary>
/// An organization using the help desk: one company, its team and its tickets. Its <see cref="Slug"/> is the
/// public address of its support form, like <c>/support/kargo-hizli</c>.
/// </summary>
public sealed partial class Tenant
{
    public const int NameMaxLength = 80;

    private Tenant()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public PlanId Plan { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset PlanChangedAt { get; private set; }

    /// <summary>
    /// The number of the newest ticket. Taken with one atomic <c>UPDATE … RETURNING</c> in the database, so two
    /// tickets opened at the same moment never share a number. The first ticket is #1001.
    /// </summary>
    public long LastTicketNumber { get; private set; } = FirstTicketNumber - 1;

    public const long FirstTicketNumber = 1001;

    /// <summary>A new organization starts on the free plan with the person who created it as its owner.</summary>
    public static (Tenant Tenant, Membership Owner) Create(string name, string slug, Guid ownerId, DateTimeOffset now)
    {
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(now),
            Name = CheckName(name),
            Slug = CheckSlug(slug),
            Plan = PlanId.Free,
            CreatedAt = now,
            PlanChangedAt = now,
        };
        return (tenant, Membership.Create(tenant.Id, ownerId, Role.Owner, now));
    }

    public void Rename(string name) => Name = CheckName(name);

    public void ChangePlan(PlanId plan, DateTimeOffset now)
    {
        if (!Enum.IsDefined(plan))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "There is no such plan.");
        }

        if (plan == Plan)
        {
            throw new DomainException(ErrorCodes.InvalidState, "The organization is already on this plan.");
        }

        Plan = plan;
        PlanChangedAt = now;
    }

    public static string CheckSlug(string slug)
    {
        var normalized = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (!SlugPattern().IsMatch(normalized))
        {
            throw new DomainException(ErrorCodes.ValidationFailed,
                "An address has 3 to 40 lowercase letters, digits or dashes and starts and ends with a letter or digit, like 'kargo-hizli'.");
        }

        return normalized;
    }

    private static string CheckName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is < 2 or > NameMaxLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"An organization name has 2 to {NameMaxLength} characters.");
        }

        return trimmed;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,38}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
