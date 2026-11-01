using Helpdesk.Core.Tickets;

namespace Helpdesk.Api.Features.Tickets;

public sealed record PersonRef(Guid Id, string Name);

public sealed record TicketResponse(
    long Number,
    string Subject,
    TicketStatus Status,
    TicketPriority Priority,
    TicketChannel Channel,
    string RequesterName,
    string RequesterEmail,
    PersonRef? Assignee,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TicketPage(IReadOnlyList<TicketResponse> Items, int Total, int Page, int PageSize);

public sealed record CommentResponse(Guid Id, string AuthorName, bool ByTeam, bool IsInternal, string Body, DateTimeOffset CreatedAt)
{
    public static CommentResponse From(Comment comment) =>
        new(comment.Id, comment.AuthorName, comment.AuthorId is not null, comment.IsInternal, comment.Body, comment.CreatedAt);
}

public sealed record CreateTicketRequest(
    string Subject, string RequesterName, string RequesterEmail, string Message, TicketPriority? Priority, Guid? AssigneeId);

/// <summary>Only the fields that are present change. <c>Unassign</c> clears the assignee.</summary>
public sealed record UpdateTicketRequest(string? Subject, TicketStatus? Status, TicketPriority? Priority, Guid? AssigneeId, bool? Unassign);

public sealed record AddCommentRequest(string Body, bool? Internal);
