namespace RepairHub.Api.Auth;

/// <summary>Claim names used in access tokens (inbound claim mapping is disabled, so these are used verbatim).</summary>
public static class AuthClaims
{
    public const string UserId = "sub";
    public const string WorkshopId = "wid";
    public const string Role = "role";
    public const string TokenVersion = "tv";
    public const string TokenId = "jti";
}

public static class AuthPolicies
{
    public const string ManageUsers = nameof(ManageUsers);
    public const string ManageWorkshop = nameof(ManageWorkshop);
}
