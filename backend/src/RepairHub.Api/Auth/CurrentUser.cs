using RepairHub.Application.Abstractions;
using RepairHub.Domain.Users;

namespace RepairHub.Api.Auth;

/// <summary>
/// Reads the caller from the validated access token. Returns nulls for anonymous requests and outside
/// HTTP requests (CLI), which makes the tenant query filter match nothing (fail closed).
/// </summary>
internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId => ReadGuid(AuthClaims.UserId);
    public Guid? WorkshopId => ReadGuid(AuthClaims.WorkshopId);

    public UserRole? Role =>
        Enum.TryParse<UserRole>(Read(AuthClaims.Role), ignoreCase: false, out var role) && Enum.IsDefined(role)
            ? role
            : null;

    private string? Read(string claimType)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        return principal?.Identity?.IsAuthenticated == true ? principal.FindFirst(claimType)?.Value : null;
    }

    private Guid? ReadGuid(string claimType) => Guid.TryParse(Read(claimType), out var id) ? id : null;
}
