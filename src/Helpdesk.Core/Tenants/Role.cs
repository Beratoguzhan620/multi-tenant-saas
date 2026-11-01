namespace Helpdesk.Core.Tenants;

/// <summary>What someone may do inside one organization. Ordered: each role can do everything the one before it can.</summary>
public enum Role
{
    /// <summary>Works on tickets.</summary>
    Agent = 0,

    /// <summary>Also manages the team and the settings.</summary>
    Admin = 1,

    /// <summary>Also changes the plan and hands over ownership.</summary>
    Owner = 2,
}
