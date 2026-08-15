namespace HR.Modules.Companies.Features.GetAuditLog;

internal sealed record GetAuditLogResponse(
    IReadOnlyList<AuditLogItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages,
    IReadOnlyList<string> AvailableEventTypes);

/// <summary>
/// AdministratorEmail/CompanyName are best-effort display resolutions (via IUserEmailDirectoryReader
/// and CompaniesDbContext respectively) — null when the id can't be resolved (e.g. a platform-wide
/// action like a background job retry has no CompanyId, or an actor's UserProfile has since been
/// removed). The underlying audit row is never dropped just because a display name can't be resolved.
/// </summary>
internal sealed record AuditLogItem(
    DateTimeOffset OccurredAt,
    string EventType,
    string EntityType,
    Guid? CompanyId,
    string? CompanyName,
    Guid? ActorUserId,
    string? AdministratorEmail,
    string? Summary);
