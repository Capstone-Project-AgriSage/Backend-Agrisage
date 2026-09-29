namespace AgriSage.Application.Common.Models;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)((TotalCount + PageSize - 1) / PageSize);
}
