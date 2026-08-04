namespace HR.Modules.Support.Features.ListSupportRequests;

internal sealed record ListSupportRequestsResponse(
    Guid Id,
    string ReferenceNumber,
    string Type,
    string Title,
    string Priority,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? LatestResponseSnippet);
