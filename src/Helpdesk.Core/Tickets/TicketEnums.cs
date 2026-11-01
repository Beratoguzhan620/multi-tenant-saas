namespace Helpdesk.Core.Tickets;

public enum TicketStatus
{
    /// <summary>Waiting for the team.</summary>
    Open,

    /// <summary>Waiting for the customer.</summary>
    Pending,

    /// <summary>Answered; the customer may still reopen it.</summary>
    Solved,

    /// <summary>Done for good.</summary>
    Closed,
}

public enum TicketPriority
{
    Low,
    Normal,
    High,
    Urgent,
}

/// <summary>Where a ticket came from.</summary>
public enum TicketChannel
{
    /// <summary>The organization's public support form.</summary>
    Web,

    /// <summary>Created by someone on the team, for example after a phone call.</summary>
    Agent,
}
