using Helpdesk.Api.Features.Tickets;
using Helpdesk.Core;
using Helpdesk.Core.Tickets;
using Helpdesk.Infrastructure.Persistence;
using Helpdesk.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Features.Public;

public sealed record PublicOrganizationResponse(string Name, string Slug);

public sealed record PublicTicketRequest(string Name, string Email, string Subject, string Message);

/// <summary>Returned once: the access key is not stored and cannot be shown again.</summary>
public sealed record PublicTicketCreated(long Number, string AccessKey);

public sealed record PublicTicketResponse(long Number, string Subject, TicketStatus Status, DateTimeOffset CreatedAt, IReadOnlyList<CommentResponse> Messages);

public sealed record PublicReplyRequest(string Message);

/// <summary>
/// The customer's side: every organization has a support form at its slug. Customers have no accounts; the key
/// they get when they open a ticket lets them follow it and reply. Internal notes never leave the team.
/// </summary>
internal static class PublicEndpoints
{
    public const string RateLimit = "public";
    public const string KeyHeader = "X-Ticket-Key";

    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public/{slug}").WithTags("Public support form").AllowAnonymous();
        group.MapGet("/", Organization).WithSummary("The organization behind a support form");
        group.MapPost("/tickets", Open).RequireRateLimiting(RateLimit).WithSummary("Open a ticket as a customer")
            .WithDescription("No account needed. Keep the access key from the response: it is the only way to follow the ticket.");
        group.MapGet("/tickets/{number:long}", Follow).RequireRateLimiting(RateLimit)
            .WithSummary("Follow your ticket").WithDescription($"Send the access key in the `{KeyHeader}` header.");
        group.MapPost("/tickets/{number:long}/replies", Reply).RequireRateLimiting(RateLimit)
            .WithSummary("Reply to the team").WithDescription($"Send the access key in the `{KeyHeader}` header. The ticket moves back to Open.");
        return app;
    }

    private static async Task<Ok<PublicOrganizationResponse>> Organization(string slug, HelpdeskDbContext db, TenantContext tenant, CancellationToken ct)
    {
        var organization = await ResolveAsync(slug, db, tenant, ct);
        return TypedResults.Ok(new PublicOrganizationResponse(organization.Name, organization.Slug));
    }

    private static async Task<Created<PublicTicketCreated>> Open(
        string slug, PublicTicketRequest request, HelpdeskDbContext db, TenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var organization = await ResolveAsync(slug, db, tenant, ct);
        var (ticket, key) = await TicketWriter.OpenAsync(
            db, organization.Id, request.Subject, request.Name, request.Email, request.Message,
            TicketChannel.Web, TicketPriority.Normal, assigneeId: null, clock.GetUtcNow(), ct);
        return TypedResults.Created($"/api/public/{organization.Slug}/tickets/{ticket.Number}", new PublicTicketCreated(ticket.Number, key));
    }

    private static async Task<Ok<PublicTicketResponse>> Follow(
        string slug, long number, [FromHeader(Name = KeyHeader)] string? key, HelpdeskDbContext db, TenantContext tenant, CancellationToken ct)
    {
        await ResolveAsync(slug, db, tenant, ct);
        var ticket = await FindAsync(db, number, key, ct);
        return TypedResults.Ok(await ResponseAsync(db, ticket, ct));
    }

    private static async Task<Created<PublicTicketResponse>> Reply(
        string slug, long number, [FromHeader(Name = KeyHeader)] string? key, PublicReplyRequest request,
        HelpdeskDbContext db, TenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var organization = await ResolveAsync(slug, db, tenant, ct);
        var ticket = await FindAsync(db, number, key, ct);
        db.Comments.Add(Comment.Write(ticket, authorId: null, ticket.RequesterName, request.Message, isInternal: false, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/public/{organization.Slug}/tickets/{number}", await ResponseAsync(db, ticket, ct));
    }

    /// <summary>The slug decides the organization for the rest of the request, exactly like a token does for the team.</summary>
    private static async Task<Core.Tenants.Tenant> ResolveAsync(string slug, HelpdeskDbContext db, TenantContext tenant, CancellationToken ct)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var organization = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Slug == normalized, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no support form at this address.");
        tenant.Set(organization.Id, userId: null, role: null);
        return organization;
    }

    /// <summary>A wrong key and a missing ticket look the same, so numbers cannot be probed.</summary>
    private static async Task<Ticket> FindAsync(HelpdeskDbContext db, long number, string? key, CancellationToken ct)
    {
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Number == number, ct);
        return ticket is not null && TicketWriter.KeyMatches(ticket, key)
            ? ticket
            : throw new DomainException(ErrorCodes.NotFound, "There is no such ticket, or the access key is wrong.");
    }

    private static async Task<PublicTicketResponse> ResponseAsync(HelpdeskDbContext db, Ticket ticket, CancellationToken ct)
    {
        var messages = await db.Comments.AsNoTracking()
            .Where(c => c.TicketId == ticket.Id && !c.IsInternal)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);
        return new PublicTicketResponse(ticket.Number, ticket.Subject, ticket.Status, ticket.CreatedAt, messages.ConvertAll(CommentResponse.From));
    }
}
