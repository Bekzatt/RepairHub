namespace RepairHub.Domain.Common;

/// <summary>
/// A business rule of the domain was violated (e.g. an invalid status transition).
/// <see cref="Code"/> is a stable, language-independent error code returned to clients.
/// </summary>
public sealed class DomainRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
