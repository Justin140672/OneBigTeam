namespace HR.Admin.Web.Models;

// App-local DTOs matching the /api/notifications/admin/operational-alerts contract — same
// "app-local DTO matching the API contract" convention as every other *Models.cs file.
public sealed record OperationalAlertListResponse(
    IReadOnlyList<OperationalAlertListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record OperationalAlertListItem(
    Guid Id,
    Guid? CompanyId,
    string Category,
    string Severity,
    string Status,
    string? Summary,
    int OccurrenceCount,
    DateTimeOffset? FirstOccurredAt,
    DateTimeOffset? LastOccurredAt,
    string? AffectedEntityType,
    Guid? AffectedEntityId,
    int AffectedItemCount,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedByUserId,
    bool IsRead);

public sealed record OperationalAlertDetailResponse(
    Guid Id,
    Guid? CompanyId,
    string Category,
    string Severity,
    string Status,
    string? Summary,
    int OccurrenceCount,
    DateTimeOffset? FirstOccurredAt,
    DateTimeOffset? LastOccurredAt,
    string? AffectedEntityType,
    Guid? AffectedEntityId,
    int AffectedItemCount,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedByUserId,
    bool IsRead,
    string? Detail,
    string? DedupKey,
    string? RecommendedAction,
    string? ActionUrl,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? AcknowledgedAt,
    Guid? AcknowledgedByUserId,
    string? ResolutionNote);

public sealed record ResolveOperationalAlertRequest(string ResolutionNote);

public sealed record ResolveOperationalAlertResponse(
    Guid Id,
    string Status,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedByUserId);

public sealed record OperationalAlertFilter(
    Guid? CompanyId = null,
    string? Category = null,
    string Status = "open",
    int Page = 1,
    int PageSize = 20);

public enum ResolveAlertOutcome
{
    Success,
    AlreadyResolved,
    ValidationFailed,
    Error,
}

public sealed record ResolveAlertResult(ResolveAlertOutcome Outcome, ResolveOperationalAlertResponse? Response);
