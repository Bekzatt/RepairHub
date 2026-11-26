namespace RepairHub.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed class PageQuery
{
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [System.ComponentModel.DataAnnotations.Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
