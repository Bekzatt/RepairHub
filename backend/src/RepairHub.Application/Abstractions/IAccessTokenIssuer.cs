using RepairHub.Domain.Users;

namespace RepairHub.Application.Abstractions;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Creates signed access tokens (JWT). Implemented in the API host.</summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(User user);
}
