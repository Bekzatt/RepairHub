using RepairHub.Application.Auth;
using RepairHub.Application.Users;
using RepairHub.Domain.Users;

namespace RepairHub.Tests.Auth;

public sealed class PasswordAndTokenUnitTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("          ")]
    [InlineData("nine-char")]
    public void PasswordPolicy_RejectsMissingOrShortPasswords(string? password) =>
        Assert.NotEmpty(PasswordPolicy.Check(password, "user@test.local"));

    [Fact]
    public void PasswordPolicy_RejectsTooLongPasswords() =>
        Assert.NotEmpty(PasswordPolicy.Check(new string('a', PasswordPolicy.MaxLength + 1), null));

    [Fact]
    public void PasswordPolicy_RejectsPasswordEqualToEmail() =>
        Assert.NotEmpty(PasswordPolicy.Check(" Long.Email@Test.Local ", "long.email@test.local"));

    [Theory]
    [InlineData("ten-chars!")]
    [InlineData("correct horse battery staple")]
    [InlineData("пароль-на-русском")]
    public void PasswordPolicy_AcceptsLongEnoughPasswordsWithoutCompositionRules(string password) =>
        Assert.Empty(PasswordPolicy.Check(password, "user@test.local"));

    [Fact]
    public void RefreshTokens_AreRandomAndStoredOnlyAsSha256Hex()
    {
        var a = RefreshTokenGenerator.NewToken();
        var b = RefreshTokenGenerator.NewToken();

        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 43); // 32 bytes, base64url
        var hash = RefreshTokenGenerator.Hash(a);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, RefreshTokenGenerator.Hash(a));
        Assert.DoesNotContain(a, hash);
    }

    [Theory]
    [InlineData(UserRole.Owner, UserRole.Owner, true)]
    [InlineData(UserRole.Owner, UserRole.Admin, true)]
    [InlineData(UserRole.Admin, UserRole.Owner, false)]
    [InlineData(UserRole.Admin, UserRole.Admin, false)]
    [InlineData(UserRole.Admin, UserRole.Technician, true)]
    [InlineData(UserRole.Admin, UserRole.Receptionist, true)]
    [InlineData(UserRole.Technician, UserRole.Technician, false)]
    [InlineData(UserRole.Receptionist, UserRole.Receptionist, false)]
    public void RoleHierarchy(UserRole actor, UserRole target, bool allowed) =>
        Assert.Equal(allowed, UserPermissions.CanManageRole(actor, target));

    [Fact]
    public void User_SensitiveChanges_BumpTokenVersion()
    {
        var now = DateTimeOffset.UtcNow;
        var user = User.Create(Guid.NewGuid(), "Name", "a@b.kz", null, UserRole.Technician, "hash", now);
        var version = user.TokenVersion;

        user.UpdateProfile("Other", "+7", now);
        Assert.Equal(version, user.TokenVersion); // profile edits do not log the user out

        user.ChangeRole(UserRole.Receptionist, now);
        user.SetPasswordHash("new-hash", now);
        user.Deactivate(now);

        Assert.Equal(version + 3, user.TokenVersion);
        Assert.False(user.IsActive);
    }
}
