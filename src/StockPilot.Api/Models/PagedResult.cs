namespace StockPilot.Api.Models;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
