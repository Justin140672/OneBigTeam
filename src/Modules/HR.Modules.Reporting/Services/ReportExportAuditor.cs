using HR.Modules.Reporting.ReportRegistry;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Services;

/// <summary>
/// Publishes REP-06 audit events for report exports via the shared cross-cutting
/// <see cref="IAuditEventPublisher"/> abstraction (HR.Infrastructure's audit store — the same
/// mechanism every other module already uses, e.g. HR.Modules.Leave.LeaveAudit). Every Export*Report
/// handler calls this after authorization has already succeeded, once for a successful export and
/// once for a post-authorization failure (e.g. an underlying generation/read error); requests
/// rejected by authorization are never routed here at all.
/// </summary>
internal sealed class ReportExportAuditor(
    IAuditEventPublisher auditPublisher,
    IClock clock,
    ICurrentUser currentUser)
{
    public Task PublishSuccessAsync(
        Guid companyId,
        string reportId,
        string format,
        int? rowCount,
        bool managerScopeApplied,
        object request,
        CancellationToken cancellationToken)
        => PublishAsync(companyId, reportId, format, rowCount, managerScopeApplied, success: true, failureReason: null, request, cancellationToken);

    public Task PublishFailureAsync(
        Guid companyId,
        string reportId,
        string format,
        bool managerScopeApplied,
        object request,
        string failureReason,
        CancellationToken cancellationToken)
        => PublishAsync(companyId, reportId, format, rowCount: null, managerScopeApplied, success: false, failureReason, request, cancellationToken);

    private async Task PublishAsync(
        Guid companyId,
        string reportId,
        string format,
        int? rowCount,
        bool managerScopeApplied,
        bool success,
        string? failureReason,
        object request,
        CancellationToken cancellationToken)
    {
        // Fail closed: if a report id isn't found in the catalogue (shouldn't happen in practice),
        // treat it as Sensitive rather than silently under-auditing it.
        var sensitivity = ReportCatalog.TryGet(reportId, out var definition)
            ? definition.Sensitivity
            : ReportSensitivity.Sensitive;

        var auditEvent = new ReportExportAuditEvent(
            companyId,
            reportId,
            format,
            currentUser.UserId,
            new DateTimeOffset(clock.UtcNow, TimeSpan.Zero),
            BuildFilters(request),
            rowCount,
            success,
            managerScopeApplied,
            sensitivity.ToString(),
            failureReason);

        await auditPublisher.PublishAsync(auditEvent, cancellationToken);
    }

    /// <summary>
    /// Structured filter criteria only — property name/value pairs from the export request (ids,
    /// dates, enum/string filter values). Never touches the generated report's rows, so no employee
    /// names or other exported PII can end up in the audit payload.
    /// </summary>
    private static IReadOnlyDictionary<string, string?> BuildFilters(object request)
    {
        var filters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in request.GetType().GetProperties())
        {
            if (string.Equals(property.Name, "CompanyId", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(property.Name, "Format", StringComparison.OrdinalIgnoreCase))
                continue;

            filters[property.Name] = property.GetValue(request)?.ToString();
        }

        return filters;
    }
}
