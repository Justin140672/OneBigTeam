namespace HR.SharedKernel;

public sealed record Pagination(int PageNumber = 1, int PageSize = 20)
{
    public int Offset => (PageNumber - 1) * PageSize;

    public static Pagination Default => new();
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int PageNumber, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}
