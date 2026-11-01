using Helpdesk.Api.Common;
using Helpdesk.Api.Features.Auth;
using Helpdesk.Core;
using Helpdesk.Core.Tickets;
using Helpdesk.Infrastructure.Persistence;
using Helpdesk.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Features.Tickets;

/// <summary>
/// The team's side of tickets. No query here mentions the organization: the global filter adds it, and
/// row-level security in PostgreSQL enforces it again.
/// </summary>
internal static class TicketEndpoints
{
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var tickets = app.MapGroup("/api/tickets").WithTags("Tickets").RequireAuthorization(AuthSetup.MemberPolicy);
        tickets.MapGet("/", List).WithSummary("List tickets")
            .WithDescription("Newest activity first. Filter by status, by assignee (`me`, `unassigned` or a member id) and by text in the subject or the ticket number.");
        tickets.MapPost("/", Create).WithSummary("Open a ticket for a customer")
            .WithDescription("For requests that arrive by phone or email. The first message is recorded as the customer's.");
        tickets.MapGet("/{number:long}", Get).WithSummary("One ticket");
        tickets.MapPatch("/{number:long}", Update).WithSummary("Change subject, status, priority or assignee");
        tickets.MapGet("/{number:long}/comments", Comments).WithSummary("The conversation, internal notes included");
        tickets.MapPost("/{number:long}/comments", Reply).WithSummary("Reply to the customer, or leave an internal note")
            .WithDescription("A reply moves the ticket to Pending (waiting for the customer); an internal note leaves the status alone.");
        return app;
    }

    private static async Task<Results<Ok<TicketPage>, ValidationProblem>> List(
        HelpdeskDbContext db, TenantContext tenant, CancellationToken ct,
        TicketStatus? status = null, string? assignee = null, string? q = null, int page = 1, int pageSize = 25)
    {
        var validation = new Validation()
            .Require(page >= 1, "page", "Pages start at 1.")
            .Require(pageSize is >= 1 and <= MaxPageSize, "pageSize", $"A page has 1 to {MaxPageSize} tickets.");
        Guid? assigneeId = null;
        if (assignee is not null and not "me" and not "unassigned")
        {
            validation.Require(Guid.TryParse(assignee, out var parsed), "assignee", "Use me, unassigned or a member id.");
            assigneeId = parsed;
        }

        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var query = db.Tickets.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(t => t.Status == status);
        }

        query = assignee switch
        {
            null => query,
            "me" => query.Where(t => t.AssigneeId == tenant.UserId),
            "unassigned" => query.Where(t => t.AssigneeId == null),
            _ => query.Where(t => t.AssigneeId == assigneeId),
        };

        if (!string.IsNullOrWhiteSpace(q))
        {
            var text = q.Trim().TrimStart('#');
            if (long.TryParse(text, out var number))
            {
                query = query.Where(t => t.Number == number);
            }
            else
            {
                var pattern = $"%{Like(text)}%";
                query = query.Where(t => EF.Functions.ILike(t.Subject, pattern, "\\")
                    || EF.Functions.ILike(t.RequesterEmail, pattern, "\\")
                    || EF.Functions.ILike(t.RequesterName, pattern, "\\"));
            }
        }

        var total = await query.CountAsync(ct);
        var items = await Project(db, query.OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.Number)
            .Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return TypedResults.Ok(new TicketPage(items, total, page, pageSize));
    }

    private static async Task<Results<Created<TicketResponse>, ValidationProblem>> Create(
        CreateTicketRequest request, HelpdeskDbContext db, TenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        if (request.AssigneeId is { } assigneeId)
        {
            await EnsureMemberAsync(db, tenant.RequiredTenantId, assigneeId, ct);
        }

        var (ticket, _) = await TicketWriter.OpenAsync(
            db, tenant.RequiredTenantId, request.Subject, request.RequesterName, request.RequesterEmail, request.Message,
            TicketChannel.Agent, request.Priority ?? TicketPriority.Normal, request.AssigneeId, clock.GetUtcNow(), ct);
        return TypedResults.Created($"/api/tickets/{ticket.Number}", await LoadAsync(db, ticket.Number, ct));
    }

    private static async Task<Ok<TicketResponse>> Get(long number, HelpdeskDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await LoadAsync(db, number, ct));

    private static async Task<Ok<TicketResponse>> Update(
        long number, UpdateTicketRequest request, HelpdeskDbContext db, TenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var ticket = await FindAsync(db, number, ct);
        var now = clock.GetUtcNow();
        if (request.Subject is not null)
        {
            ticket.Rename(request.Subject, now);
        }

        if (request.Priority is { } priority)
        {
            ticket.ChangePriority(priority, now);
        }

        if (request.Unassign == true)
        {
            ticket.Assign(null, now);
        }
        else if (request.AssigneeId is { } assigneeId)
        {
            await EnsureMemberAsync(db, tenant.RequiredTenantId, assigneeId, ct);
            ticket.Assign(assigneeId, now);
        }

        if (request.Status is { } status)
        {
            ticket.ChangeStatus(status, now);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadAsync(db, number, ct));
    }

    private static async Task<Ok<List<CommentResponse>>> Comments(long number, HelpdeskDbContext db, CancellationToken ct)
    {
        var ticket = await FindAsync(db, number, ct);
        var comments = await db.Comments.AsNoTracking()
            .Where(c => c.TicketId == ticket.Id)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(comments.ConvertAll(CommentResponse.From));
    }

    private static async Task<Created<CommentResponse>> Reply(
        long number, AddCommentRequest request, HelpdeskDbContext db, TenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var ticket = await FindAsync(db, number, ct);
        var author = await db.Users.AsNoTracking().Where(u => u.Id == tenant.UserId).Select(u => u.DisplayName).SingleAsync(ct);
        var comment = Comment.Write(ticket, tenant.UserId, author, request.Body, request.Internal ?? false, clock.GetUtcNow());
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/tickets/{number}/comments", CommentResponse.From(comment));
    }

    internal static async Task<Ticket> FindAsync(HelpdeskDbContext db, long number, CancellationToken ct) =>
        await db.Tickets.SingleOrDefaultAsync(t => t.Number == number, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, $"There is no ticket #{number}.");

    private static async Task<TicketResponse> LoadAsync(HelpdeskDbContext db, long number, CancellationToken ct) =>
        await Project(db, db.Tickets.AsNoTracking().Where(t => t.Number == number)).SingleOrDefaultAsync(ct)
        ?? throw new DomainException(ErrorCodes.NotFound, $"There is no ticket #{number}.");

    private static IQueryable<TicketResponse> Project(HelpdeskDbContext db, IQueryable<Ticket> tickets) =>
        from t in tickets
        join u in db.Users on t.AssigneeId equals u.Id into assignees
        from u in assignees.DefaultIfEmpty()
        select new TicketResponse(
            t.Number, t.Subject, t.Status, t.Priority, t.Channel, t.RequesterName, t.RequesterEmail,
            u == null ? null : new PersonRef(u.Id, u.DisplayName),
            t.CreatedAt, t.UpdatedAt);

    /// <summary>Tickets can only be assigned to people in the same organization.</summary>
    private static async Task EnsureMemberAsync(HelpdeskDbContext db, Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (!await db.Memberships.AnyAsync(m => m.TenantId == tenantId && m.UserId == userId, ct))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Tickets can only be assigned to members of this organization.");
        }
    }

    private static string Like(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
