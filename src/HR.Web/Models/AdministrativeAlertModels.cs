namespace HR.Web.Models;

// ADM-03: administrative alerts & incidents inbox. Mirrors the API contract for
// GET /api/companies/{companyId}/administrative-alerts and its action endpoints.

public record AdministrativeAlertFilter(
    string? Severity = null,
    string? Category = null,
    string? Status = null,
    bool? IsRead = null,
    DateOnly? OccurredFrom = null,
    DateOnly? OccurredTo = null);

public record GetAdministrativeAlertsResponse(
    int UnreadCount,
    List<AdministrativeAlertRowModel> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

public record AdministrativeAlertRowModel(
    Guid Id,
    string Severity,
    string Category,
    string Summary,
    string? Detail,
    int OccurrenceCount,
    DateTimeOffset FirstOccurredAt,
    DateTimeOffset LastOccurredAt,
    string? AffectedEntityType,
    Guid? AffectedEntityId,
    string? RecommendedAction,
    string? ActionUrl,
    bool IsRead,
    string Status,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote);

public record AdministrativeAlertUnreadCountResponse(int Count);

public record ResolveAdministrativeAlertRequest(string? ResolutionNote);

public enum AdministrativeAlertActionResult
{
    Success,
    Conflict,
    NotFound,
    Failed,
}
