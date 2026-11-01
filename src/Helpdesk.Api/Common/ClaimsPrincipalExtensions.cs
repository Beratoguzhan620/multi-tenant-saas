using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Helpdesk.Api.Common;

internal static class ClaimsPrincipalExtensions
{
    public const string TenantClaim = "tid";

    /// <summary>The signed-in user. Endpoints that call this require authentication, so the claim is present.</summary>
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("The principal has no subject claim."));

    /// <summary>The organization the token was issued for, if any.</summary>
    public static Guid? TenantId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(TenantClaim), out var id) ? id : null;
}
