using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RepairHub.Application.Abstractions;
using RepairHub.Application.Auth;
using RepairHub.Application.Common;
using RepairHub.Domain.Users;
using RepairHub.Domain.Workshops;

namespace RepairHub.Application.Workshops;

public sealed class CreateWorkshopRequest
{
    [Required, MaxLength(200)]
    public string WorkshopName { get; init; } = string.Empty;

    [MaxLength(500)]
    public string? Address { get; init; }

    [MaxLength(32)]
    public string? Phone { get; init; }

    [Required, RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency must be a 3-letter ISO 4217 code.")]
    public string Currency { get; init; } = Workshop.DefaultCurrency;

    [Required, IanaTimeZone]
    public string TimeZone { get; init; } = Workshop.DefaultTimeZone;

    [Required, MaxLength(200)]
    public string OwnerFullName { get; init; } = string.Empty;

    [Required, EmailAddress, MaxLength(254)]
    public string OwnerEmail { get; init; } = string.Empty;

    [Required, MaxLength(PasswordPolicy.MaxLength)]
    public string OwnerPassword { get; init; } = string.Empty;

    /// <summary>
    /// Safety switch: by default provisioning refuses to run when any workshop already exists,
    /// so re-running the first-time setup cannot create a second tenant by accident.
    /// </summary>
    public bool AllowAdditionalWorkshop { get; init; }
}

public sealed record ProvisionedWorkshop(Guid WorkshopId, Guid OwnerId);

/// <summary>
/// Creates a workshop together with its first Owner. Used only by the server-side
/// <c>create-workshop</c> command — never exposed over HTTP.
/// </summary>
public sealed class WorkshopProvisioningService(
    IAppDbContext db, IPasswordHasher passwordHasher, TimeProvider time, ILogger<WorkshopProvisioningService> logger)
{
    public async Task<ProvisionedWorkshop> CreateAsync(CreateWorkshopRequest request, CancellationToken ct)
    {
        RequestValidator.ValidateOrThrow(request);
        PasswordPolicy.EnsureValid(request.OwnerPassword, request.OwnerEmail, "ownerPassword");

        var now = time.GetUtcNow();
        var ownerEmail = User.NormalizeEmail(request.OwnerEmail);
        var workshopName = request.WorkshopName.Trim();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serializes concurrent runs of the command so the checks below cannot race.
        await db.LockProvisioningAsync(ct);

        // Tenant filter bypass (allow-listed): provisioning runs without a current workshop by definition.
        var workshops = db.Workshops.AcrossAllWorkshops();
        if (!request.AllowAdditionalWorkshop && await workshops.AnyAsync(ct))
            throw new ConflictException(ErrorCodes.WorkshopsExist,
                "A workshop already exists. Use --allow-additional-workshop to create another one.");

        if (await workshops.AnyAsync(w => w.Name.ToLower() == workshopName.ToLower(), ct))
            throw new ConflictException(ErrorCodes.WorkshopNameTaken, "A workshop with this name already exists.");

        if (await db.Users.AcrossAllWorkshops().AnyAsync(u => u.NormalizedEmail == ownerEmail, ct))
            throw new ConflictException(ErrorCodes.EmailTaken, "A user with this email already exists.");

        var workshop = Workshop.Create(workshopName, request.Address, request.Phone, request.Currency, request.TimeZone, now);
        var owner = User.Create(
            workshop.Id, request.OwnerFullName, request.OwnerEmail, phone: null, UserRole.Owner,
            passwordHasher.Hash(request.OwnerPassword), now);

        db.Workshops.Add(workshop);
        db.Users.Add(owner);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Provisioned workshop {WorkshopId} with owner {UserId}", workshop.Id, owner.Id);
        return new ProvisionedWorkshop(workshop.Id, owner.Id);
    }
}
