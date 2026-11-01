using System.Security.Claims;
using Helpdesk.Api.Common;
using Helpdesk.Infrastructure.Tenancy;
using Helpdesk.Api.Features.Auth;
using Helpdesk.Core;
using Helpdesk.Core.Tenants;
using Helpdesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Api.Features.Organizations;

public sealed record CreateOrganizationRequest(string Name, string Slug);

public sealed record RenameOrganizationRequest(string Name);

public sealed record OrganizationResponse(Guid Id, string Name, string Slug, PlanId Plan, DateTimeOffset CreatedAt)
{
    public static OrganizationResponse From(Tenant tenant) => new(tenant.Id, tenant.Name, tenant.Slug, tenant.Plan, tenant.CreatedAt);
}

internal static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/organizations", Create).WithTags("Organizations").RequireAuthorization()
            .WithSummary("Create another organization")
            .WithDescription("You become its owner. The response is a session for the new organization.");

        var current = app.MapGroup("/api/organization").WithTags("Organizations");
        current.MapGet("/", Get).RequireAuthorization(AuthSetup.MemberPolicy).WithSummary("The organization you are working in");
        current.MapPatch("/", Rename).RequireAuthorization(AuthSetup.AdminPolicy).WithSummary("Rename it (admin)");
        return app;
    }

    private static async Task<Results<Created<SessionResponse>, ValidationProblem>> Create(
        CreateOrganizationRequest request, ClaimsPrincipal principal, HelpdeskDbContext db, TokenService tokens, TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Name), "name", "Name the organization.")
            .Require(!string.IsNullOrWhiteSpace(request.Slug), "slug", "Choose an address for the organization.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var user = await db.Users.SingleAsync(u => u.Id == principal.UserId(), ct);
        var (tenant, owner) = Tenant.Create(request.Name, request.Slug, user.Id, clock.GetUtcNow());
        db.Tenants.Add(tenant);
        db.Memberships.Add(owner);
        await AuthEndpoints.SaveAsync(db, ct);
        return TypedResults.Created("/api/organization", await Sessions.CreateAsync(db, tokens, user, tenant.Id, ct));
    }

    private static async Task<Ok<OrganizationResponse>> Get(TenantContext tenant, HelpdeskDbContext db, CancellationToken ct) =>
        TypedResults.Ok(OrganizationResponse.From(await Load(db, tenant, ct)));

    private static async Task<Ok<OrganizationResponse>> Rename(
        RenameOrganizationRequest request, TenantContext tenant, HelpdeskDbContext db, CancellationToken ct)
    {
        var organization = await Load(db, tenant, ct);
        organization.Rename(request.Name);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(OrganizationResponse.From(organization));
    }

    private static async Task<Tenant> Load(HelpdeskDbContext db, TenantContext tenant, CancellationToken ct) =>
        await db.Tenants.SingleOrDefaultAsync(t => t.Id == tenant.RequiredTenantId, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "The organization no longer exists.");
}
