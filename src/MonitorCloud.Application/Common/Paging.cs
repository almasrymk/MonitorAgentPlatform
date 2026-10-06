using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));

    /// <summary>Resolves a <c>sort</c> value (optionally prefixed with '-') against the allowed keys.</summary>
    public static Result<(string Key, bool Descending)> ResolveSort(string? sort, string defaultKey, IReadOnlyCollection<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        if (string.IsNullOrWhiteSpace(sort))
            return (defaultKey, false);

        var descending = sort.StartsWith('-');
        var key = descending ? sort[1..] : sort;
        var match = allowed.FirstOrDefault(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
        return match is null ? CommonErrors.InvalidSort(sort, allowed) : (match, descending);
    }

    public static async Task<PagedResult<T>> ToPageAsync<T>(this IQueryable<T> query, int page, int pageSize, Func<IQueryable<T>, CancellationToken, Task<int>> count, Func<IQueryable<T>, CancellationToken, Task<List<T>>> list, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(count);
        ArgumentNullException.ThrowIfNull(list);
        var total = await count(query, cancellationToken);
        var items = await list(query.Skip((page - 1) * pageSize).Take(pageSize), cancellationToken);
        return new PagedResult<T>(items, total, page, pageSize);
    }
}
