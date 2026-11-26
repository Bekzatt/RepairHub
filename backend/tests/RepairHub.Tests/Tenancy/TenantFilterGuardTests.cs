using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RepairHub.Application.Abstractions;
using RepairHub.Domain.Common;
using RepairHub.Domain.Users;
using RepairHub.Infrastructure.Persistence;

namespace RepairHub.Tests.Tenancy;

/// <summary>
/// Static guards that keep tenant isolation from eroding as the model grows. No database needed.
/// </summary>
public sealed class TenantFilterGuardTests
{
    /// <summary>
    /// Every file allowed to read across workshops, with the reason. Adding an entry is a deliberate,
    /// reviewable decision; anything else calling the bypass fails this test.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedBypassCallers = new()
    {
        ["AuthService.cs"] = "login by email, refresh by token: the workshop is not known yet",
        ["AccessTokenSessionValidator.cs"] = "token validation runs before the request has a current user",
        ["WorkshopProvisioning.cs"] = "bootstrap command: duplicate checks across all workshops",
    };

    [Fact]
    public void EveryTenantOwnedEntity_HasTheTenantQueryFilter()
    {
        using var context = CreateModelOnlyContext();

        var entityTypes = context.Model.GetEntityTypes().ToList();
        var tenantOwned = entityTypes.Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)).ToList();
        Assert.NotEmpty(tenantOwned);

        foreach (var entityType in tenantOwned)
            Assert.True(HasTenantFilter(entityType), $"{entityType.ClrType.Name} is missing the tenant filter.");

        Assert.True(HasTenantFilter(context.Model.FindEntityType(typeof(Domain.Workshops.Workshop))!));
    }

    [Fact]
    public void EveryEntityWithAWorkshopId_ImplementsITenantOwned()
    {
        using var context = CreateModelOnlyContext();

        var offenders = context.Model.GetEntityTypes()
            .Where(t => t.FindProperty("WorkshopId") is not null && !typeof(ITenantOwned).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TenantFilterBypass_IsUsedOnlyByAllowListedFiles()
    {
        var sourceRoot = Path.Combine(FindBackendRoot(), "src");
        var files = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        var ignoreFiltersCallers = files
            .Where(f => File.ReadAllText(f).Contains("IgnoreQueryFilters(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Equal(["TenantFilterBypass.cs"], ignoreFiltersCallers);

        var bypassCallers = files
            .Where(f => File.ReadAllText(f).Contains(".AcrossAllWorkshops()", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f)!)
            .ToHashSet();
        Assert.Subset(AllowedBypassCallers.Keys.ToHashSet(), bypassCallers);
    }

    private static bool HasTenantFilter(IReadOnlyEntityType entityType) =>
        entityType.FindDeclaredQueryFilter(TenantFilterBypass.FilterName) is not null;

    private static AppDbContext CreateModelOnlyContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only")
                .UseSnakeCaseNamingConvention()
                .Options,
            new NoCurrentUser());

    private static string FindBackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RepairHub.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("RepairHub.slnx not found above the test output.");
    }

    private sealed class NoCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? WorkshopId => null;
        public UserRole? Role => null;
    }
}
