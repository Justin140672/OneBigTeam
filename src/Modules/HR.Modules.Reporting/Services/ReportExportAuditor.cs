using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.ReportRegistry;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

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
    IAdministrativeAlertWriter administrativeAlertWriter,
    IClock clock,
    ICurrentUser currentUser,
    ILogger<ReportExportAuditor> logger)
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

    public async Task PublishFailureAsync(
        Guid companyId,
        string reportId,
        string format,
        bool managerScopeApplied,
        object request,
        string failureReason,
        CancellationToken cancellationToken)
    {
        await PublishAsync(companyId, reportId, format, rowCount: null, managerScopeApplied, success: false, failureReason, request, cancellationToken);

        try
        {
            await administrativeAlertWriter.RaiseAsync(new RaiseAdministrativeAlertCommand(
                companyId,
                AdministrativeAlertSeverity.Warning,
                AdministrativeAlertCategory.ReportGeneration,
                $"Report generation failed: {reportId}",
                $"A {format} export of report '{reportId}' failed after authorization ({failureReason}).",
                new DateTimeOffset(clock.UtcNow, TimeSpan.Zero),
                DedupKey: $"report-generation:{reportId}",
                AffectedEntityType: "Report",
                AffectedEntityId: null,
                RecommendedAction: "Retry the export; if it keeps failing, review the report's data source.",
                ActionUrl: null),
                cancellationToken);
        }
        catch (Exception alertEx)
        {
            logger.LogWarning(alertEx,
                "ReportExportAuditor: failed to raise administrative alert for report generation failure of {ReportId}.",
                reportId);
        }
    }

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
