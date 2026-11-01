using Helpdesk.Api.Features.Audit;
using Helpdesk.Api.Features.Auth;
using Helpdesk.Api.Features.Tickets;
using Helpdesk.Core;
using Helpdesk.Core.Audit;
using Helpdesk.Core.Tenants;
using Helpdesk.Core.Tickets;
using Helpdesk.Core.Usage;
using Helpdesk.Infrastructure.Persistence;
using Helpdesk.Infrastructure.Storage;
using Helpdesk.Infrastructure.Tenancy;
using Helpdesk.Infrastructure.Usage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Features.Attachments;

public sealed record AttachmentResponse(Guid Id, string FileName, string ContentType, long SizeBytes, DateTimeOffset CreatedAt)
{
    public static AttachmentResponse From(Attachment a) => new(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAt);
}

/// <summary>
/// Files on tickets. The plan's storage is reserved with the same conditional update as every other limit,
/// inside the transaction that stores the attachment, so a failed upload gives the space back by itself.
/// </summary>
internal static class AttachmentEndpoints
{
    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var onTicket = app.MapGroup("/api/tickets/{number:long}/attachments").WithTags("Attachments").RequireAuthorization(AuthSetup.MemberPolicy);
        onTicket.MapGet("/", List).WithSummary("Files on a ticket");
        onTicket.MapPost("/", Upload).DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(Attachment.MaxBytes + (64 * 1024)))
            .WithSummary("Attach a file").WithDescription("Multipart form with one `file`, up to 10 MB. Counts against the plan's storage.");

        var single = app.MapGroup("/api/attachments").WithTags("Attachments").RequireAuthorization(AuthSetup.MemberPolicy);
        single.MapGet("/{id:guid}", Download).WithSummary("Download a file").WithDescription("Always as a download, never rendered inline.");
        single.MapDelete("/{id:guid}", Delete).WithSummary("Delete a file").WithDescription("By the person who added it, or an admin. Frees its storage.");
        return app;
    }

    private static async Task<Ok<List<AttachmentResponse>>> List(long number, HelpdeskDbContext db, CancellationToken ct)
    {
        var ticket = await TicketEndpoints.FindAsync(db, number, ct);
        var attachments = await db.Attachments.AsNoTracking().Where(a => a.TicketId == ticket.Id).OrderBy(a => a.CreatedAt).ToListAsync(ct);
        return TypedResults.Ok(attachments.ConvertAll(AttachmentResponse.From));
    }

    private static async Task<Created<AttachmentResponse>> Upload(
        long number, IFormFile? file, HelpdeskDbContext db, TenantContext tenant, IFileStore store, TimeProvider clock, CancellationToken ct)
    {
        if (file is null)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Send the file as multipart form data in a field named 'file'.");
        }

        var now = clock.GetUtcNow();
        var tenantId = tenant.RequiredTenantId;
        var ticket = await TicketEndpoints.FindAsync(db, number, ct);
        var attachment = Attachment.Create(ticket, file.FileName, file.ContentType, file.Length, tenant.UserId, now);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var plan = Plan.For(await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Plan).SingleAsync(ct));
        if (!await UsageCounters.TryAddAsync(db, tenantId, UsageMetric.StorageBytes, UsageMetric.AllTime, attachment.SizeBytes, plan.StorageBytes, ct))
        {
            throw new DomainException(ErrorCodes.PlanLimitReached,
                $"This file does not fit in the {plan.StorageBytes / Plan.MiB} MB of the {plan.Name} plan. Delete some files or upgrade.");
        }

        try
        {
            await using (var content = file.OpenReadStream())
            {
                await store.SaveAsync(attachment.StorageKey, content, ct);
            }

            db.Attachments.Add(attachment);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await store.DeleteAsync(attachment.StorageKey, CancellationToken.None);
            throw;
        }

        return TypedResults.Created($"/api/attachments/{attachment.Id}", AttachmentResponse.From(attachment));
    }

    private static async Task<IResult> Download(Guid id, HelpdeskDbContext db, IFileStore store, HttpContext http, CancellationToken ct)
    {
        var attachment = await FindAsync(db, id, ct);
        var stream = await store.OpenReadAsync(attachment.StorageKey, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "The file is missing from storage.");
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.Stream(stream, attachment.ContentType, attachment.FileName);
    }

    private static async Task<NoContent> Delete(
        Guid id, HelpdeskDbContext db, TenantContext tenant, IFileStore store, TimeProvider clock, CancellationToken ct)
    {
        var attachment = await FindAsync(db, id, ct);
        if (attachment.UploadedBy != tenant.UserId && tenant.Role < Role.Admin)
        {
            throw new DomainException(ErrorCodes.Forbidden, "Only the person who added the file, or an admin, deletes it.");
        }

        var now = clock.GetUtcNow();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            db.Attachments.Remove(attachment);
            await UsageCounters.ReleaseAsync(db, tenant.RequiredTenantId, UsageMetric.StorageBytes, UsageMetric.AllTime, attachment.SizeBytes, ct);
            var ticketNumber = await db.Tickets.Where(t => t.Id == attachment.TicketId).Select(t => t.Number).SingleAsync(ct);
            await AuditLog.AddAsync(db, tenant, AuditActions.AttachmentDeleted, $"#{ticketNumber}", attachment.FileName, now, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        // After the commit: a crash in between leaves an orphaned file, never a row without its file.
        await store.DeleteAsync(attachment.StorageKey, CancellationToken.None);
        return TypedResults.NoContent();
    }

    private static async Task<Attachment> FindAsync(HelpdeskDbContext db, Guid id, CancellationToken ct) =>
        await db.Attachments.SingleOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "There is no such file.");
}
