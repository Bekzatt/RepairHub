namespace RepairHub.Domain.Workshops;

/// <summary>A repair service center. The tenant boundary of the system.</summary>
public sealed class Workshop
{
    public const string DefaultCurrency = "KZT";
    public const string DefaultTimeZone = "Asia/Almaty";

    private Workshop() { } // EF Core

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Address { get; private set; }
    public string? Phone { get; private set; }
    public string Currency { get; private set; } = DefaultCurrency;
    public string TimeZone { get; private set; } = DefaultTimeZone;
    public DateTimeOffset CreatedAt { get; private set; }

    public static Workshop Create(
        string name, string? address, string? phone, string currency, string timeZone, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            Name = name.Trim(),
            Address = Normalize(address),
            Phone = Normalize(phone),
            Currency = currency.Trim().ToUpperInvariant(),
            TimeZone = timeZone.Trim(),
            CreatedAt = now,
        };

    public void UpdateDetails(string name, string? address, string? phone, string currency, string timeZone)
    {
        Name = name.Trim();
        Address = Normalize(address);
        Phone = Normalize(phone);
        Currency = currency.Trim().ToUpperInvariant();
        TimeZone = timeZone.Trim();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
