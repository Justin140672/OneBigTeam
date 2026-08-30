using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Reporting.Features.GetComplianceCentre;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Reporting.Jobs;

/// <summary>
/// ADM-03: daily scan that turns the ADM-02 Compliance Centre's <b>overdue</b> items into
/// administrative alerts (category <see cref="AdministrativeAlertCategory.Compliance"/>) for every
/// active company. Reuses <see cref="GetComplianceCentreHandler"/> wholesale so the reader
/// composition, per-category classification and the single-clock overdue/due-soon rule stay in one
/// place — this job only decides "overdue count &gt; 0 ⇒ raise a grouped alert".
///
/// Grouping: one alert per <c>(company, compliance category)</c> via a stable
/// <c>compliance:{Category}</c> dedup key, so a re-run on a still-unresolved situation just bumps
/// the existing alert's occurrence count (see <see cref="IAdministrativeAlertWriter"/>), and a
/// category that has been cleared simply stops being re-raised (any prior alert is left for an
/// administrator to resolve). Best-effort: one company's failure never blocks the rest of the batch.
/// </summary>
internal sealed class GenerateComplianceAlertsJob(
    IActiveCompanyDirectory activeCompanyDirectory,
    GetComplianceCentreHandler complianceCentreHandler,
    IAdministrativeAlertWriter administrativeAlertWriter,
    IClock clock,
    ILogger<GenerateComplianceAlertsJob> logger)
{
    private const int MaxExamplesInDetail = 3;

    public async Task ExecuteAsync()
    {
        var companyIds = await activeCompanyDirectory.GetActiveCompanyIdsAsync(CancellationToken.None);

        foreach (var companyId in companyIds)
        {
            try
            {
                await ProcessCompanyAsync(companyId);
            }
            catch (Exception ex)
            {
                // Isolate one company's failure from the batch; Hangfire retries the whole job and
                // the alert writer's dedup makes any re-raise idempotent.
                logger.LogError(ex,
                    "ADM-03 compliance alert scan failed for company {CompanyId}", companyId);
            }
        }
    }

    private async Task ProcessCompanyAsync(Guid companyId)
    {
        var result = await complianceCentreHandler.HandleAsync(
            new GetComplianceCentreRequest(companyId), CancellationToken.None);

        if (result.IsFailure)
        {
            logger.LogWarning(
                "ADM-03 compliance alert scan: compliance centre query failed for company {CompanyId}: {Error}",
                companyId, result.Error.Message);
            return;
        }

        var response = result.Value!;
        var occurredAt = clock.UtcNowOffset();

        foreach (var category in response.CategorySummaries.Where(c => c.Overdue > 0))
        {
            var examples = response.Items
                .Where(i => i.Category == category.Category
                            && string.Equals(i.Severity, nameof(ComplianceSeverity.Overdue), StringComparison.OrdinalIgnoreCase))
                .Take(MaxExamplesInDetail)
                .Select(i => $"{i.EmployeeName}: {i.Detail}")
                .ToList();

            var more = category.Overdue - examples.Count;
            var detail = examples.Count == 0
                ? $"{category.Overdue} overdue {category.CategoryLabel} item(s) require attention."
                : string.Join(
                    Environment.NewLine,
                    examples.Append(more > 0 ? $"…and {more} more." : null!).Where(x => x is not null));

            await administrativeAlertWriter.RaiseAsync(
                new RaiseAdministrativeAlertCommand(
                    companyId,
                    AdministrativeAlertSeverity.Warning,
                    AdministrativeAlertCategory.Compliance,
                    $"{category.Overdue} overdue compliance item(s): {category.CategoryLabel}",
                    detail,
                    occurredAt,
                    DedupKey: $"compliance:{category.Category}",
                    AffectedEntityType: "ComplianceCategory",
                    AffectedEntityId: null,
                    RecommendedAction: "Review and clear the overdue items in the Compliance Centre.",
                    ActionUrl: $"/companies/{companyId}/reporting/compliance-centre"),
                CancellationToken.None);
        }
    }
}
