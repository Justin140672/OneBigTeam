using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Features.GetAdministrativeAlerts;

// ADM-03: shared admin incidents inbox — no per-user filter. Filters mirror GetMyNotifications'
// pagination shape (PageNumber/PageSize) plus severity/category/status/read and a LastOccurredAt
// date range.
internal sealed class GetAdministrativeAlertsRequest
{
    public Guid CompanyId { get; init; }

    public AdministrativeAlertSeverity? Severity { get; init; }

    public AdministrativeAlertCategory? Category { get; init; }

    public AdministrativeAlertStatus? Status { get; init; }

    public bool? IsRead { get; init; }

    public DateTimeOffset? OccurredFrom { get; init; }

    public DateTimeOffset? OccurredTo { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}
