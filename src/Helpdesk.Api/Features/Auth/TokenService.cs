using Helpdesk.Core.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Helpdesk.Api.Features.Auth;

internal sealed class TokenService(SigningKeyProvider keys, IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>A token for one person in one organization. Roles are not in the token; they are checked on every request.</summary>
    public (string Token, DateTimeOffset ExpiresAt) Issue(User user, Guid? tenantId)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(settings.AccessTokenMinutes);
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
            [JwtRegisteredClaimNames.Name] = user.DisplayName,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
        };
        if (tenantId is { } tid)
        {
            claims[Common.ClaimsPrincipalExtensions.TenantClaim] = tid.ToString();
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(keys.Key, SecurityAlgorithms.HmacSha256),
            Claims = claims,
        });
        return (token, expiresAt);
    }
}
