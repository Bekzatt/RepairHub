using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RepairHub.Application.Abstractions;
using RepairHub.Domain.Users;

namespace RepairHub.Api.Auth;

internal sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider time) : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessToken Issue(User user)
    {
        var jwt = options.Value;
        var now = time.GetUtcNow();
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(jwt.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [AuthClaims.UserId] = user.Id.ToString(),
                [AuthClaims.WorkshopId] = user.WorkshopId.ToString(),
                [AuthClaims.Role] = user.Role.ToString(),
                [AuthClaims.TokenVersion] = user.TokenVersion.ToString(CultureInfo.InvariantCulture),
                [AuthClaims.TokenId] = Guid.NewGuid().ToString(),
            },
        });

        return new AccessToken(token, expires);
    }
}
