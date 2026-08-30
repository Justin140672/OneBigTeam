using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.ReportRegistry;
using HR.SharedKernel;

namespace HR.Modules.Reporting.Features.GetComplianceCentre;

/// <summary>
/// ADM-02 Consolidated Compliance Centre. Composes several company-wide, per-module contract
/// readers (expiring employee documents, missing required documents, outstanding document requests,
/// pending probation reviews) into a single prioritised compliance view, mirroring how
/// GetWorkloadActions composes <see cref="IWorkloadActionProvider"/> — without HR.Modules.Reporting
/// referencing any owning module implementation.
///
/// Authorization is the endpoint's compliance:view policy (HR Administrator only). Every reader is
/// company-scoped by the route company id, so counts and records never cross a company boundary.
/// Severity (overdue / due-soon / informational) is computed here from a single clock so every
/// category is judged consistently.
/// </summary>
internal sealed class GetComplianceCentreHandler(
    IExpiringEmployeeDocumentReader expiringDocumentReader,
    IDocumentComplianceReportReader documentComplianceReportReader,
    IOutstandingDocumentRequestComplianceReader outstandingDocumentRequestReader,
    IProbationReviewComplianceReader probationReviewReader,
    IEmployeeDirectoryReader employeeDirectoryReader,
    IClock clock)
{
    // A single window used for every category so "due soon" means the same thing everywhere.
    private const int DueSoonWindowDays = 30;

    public async Task<Result<GetComplianceCentreResponse>> HandleAsync(
        GetComplianceCentreRequest request,
        CancellationToken cancellationToken)
    {
        var companyId = request.CompanyId;
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var horizon = today.AddDays(DueSoonWindowDays);

        var expiringDocs = await expiringDocumentReader.GetExpiringEmployeeDocumentsAsync(
            companyId, today, DueSoonWindowDays, cancellationToken);
        var complianceReport = await documentComplianceReportReader.GetDocumentComplianceReportAsync(
            companyId, positionProfileId: null, cancellationToken);
        var outstandingRequests = await outstandingDocumentRequestReader.GetOutstandingDocumentRequestsAsync(
            companyId, cancellationToken);
        var probationReviews = await probationReviewReader.GetPendingProbationReviewsAsync(
            companyId, cancellationToken);

        var raw = new List<RawItem>();

        foreach (var doc in expiringDocs)
        {
            var category = doc.Kind switch
            {
                ComplianceDocumentKind.Immigration => ComplianceCategory.ExpiringVisa,
                ComplianceDocumentKind.Certification => ComplianceCategory.ExpiringCertification,
                _ => ComplianceCategory.ExpiringOtherDocument
            };

            raw.Add(new RawItem(
                doc.EmployeeId,
                category,
                $"{doc.DocumentTypeName}: {doc.DocumentTitle} expires {doc.ExpiryDate:yyyy-MM-dd}",
                doc.ExpiryDate,
                DocumentsDeepLink(companyId, doc.EmployeeId)));
        }

        foreach (var item in complianceReport.Where(i => i.MissingCount > 0))
        {
            foreach (var typeName in item.MissingDocumentTypeNames)
            {
                raw.Add(new RawItem(
                    item.EmployeeId,
                    ComplianceCategory.MissingRequiredDocument,
                    $"Missing required document: {typeName}",
                    null,
                    DocumentsDeepLink(companyId, item.EmployeeId)));
            }
        }

        foreach (var req in outstandingRequests)
        {
            var detail = req.IsMandatory
                ? $"Outstanding document request (mandatory): {req.DocumentTypeName}"
                : $"Outstanding document request: {req.DocumentTypeName}";

            raw.Add(new RawItem(
                req.EmployeeId,
                ComplianceCategory.OutstandingDocumentRequest,
                detail,
                req.DueDate,
                DocumentsDeepLink(companyId, req.EmployeeId)));
        }

        foreach (var review in probationReviews)
        {
            // Only surface reviews that are overdue or fall due within the window — a review due
            // months out is not yet a compliance action.
            if (review.DueDate > horizon)
                continue;

            raw.Add(new RawItem(
                review.EmployeeId,
                ComplianceCategory.ProbationReview,
                $"{review.ReviewType} probation review due {review.DueDate:yyyy-MM-dd}",
                review.DueDate,
                EmployeeDeepLink(companyId, review.EmployeeId)));
        }

        // One directory call resolves employee display name + department for enrichment and, when a
        // manager filter is supplied, restricts the set to that manager's reporting line. This never
        // widens what is returned — it only narrows.
        var directory = await employeeDirectoryReader.GetEmployeeDirectoryAsync(
            companyId,
            new ReportFilterCriteria(ManagerId: request.ManagerId),
            new Pagination(1, 5000),
            sortBy: null,
            sortDescending: false,
            cancellationToken);

        var directoryById = directory.Items
            .GroupBy(i => i.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        var restrictToDirectory = request.ManagerId is not null;

        var enriched = raw
            .Where(r => !restrictToDirectory || directoryById.ContainsKey(r.EmployeeId))
            .Select(r =>
            {
                directoryById.TryGetValue(r.EmployeeId, out var emp);
                return new EnrichedItem(
                    r.EmployeeId,
                    emp?.Name ?? "Unknown employee",
                    emp?.Department,
                    r.Category,
                    r.Detail,
                    r.DueDate,
                    ComputeSeverity(r.DueDate, today),
                    r.DeepLinkUrl);
            })
            .ToList();

        IEnumerable<EnrichedItem> filtered = enriched;

        if (!string.IsNullOrWhiteSpace(request.Category)
            && Enum.TryParse<ComplianceCategory>(request.Category, ignoreCase: true, out var categoryFilter))
        {
            filtered = filtered.Where(i => i.Category == categoryFilter);
        }

        if (!string.IsNullOrWhiteSpace(request.Severity)
            && Enum.TryParse<ComplianceSeverity>(request.Severity, ignoreCase: true, out var severityFilter))
        {
            filtered = filtered.Where(i => i.Severity == severityFilter);
        }

        if (!string.IsNullOrWhiteSpace(request.Department))
        {
            filtered = filtered.Where(i => i.Department is not null
                && i.Department.Contains(request.Department, StringComparison.OrdinalIgnoreCase));
        }

        if (request.DueDateStart is { } start)
            filtered = filtered.Where(i => i.DueDate is not null && i.DueDate >= start);

        if (request.DueDateEnd is { } end)
            filtered = filtered.Where(i => i.DueDate is not null && i.DueDate <= end);

        var finalItems = filtered.ToList();

        var summary = new ComplianceCentreSummary(
            finalItems.Count,
            finalItems.Count(i => i.Severity == ComplianceSeverity.Overdue),
            finalItems.Count(i => i.Severity == ComplianceSeverity.DueSoon),
            finalItems.Count(i => i.Severity == ComplianceSeverity.Informational));

        var categorySummaries = Enum.GetValues<ComplianceCategory>()
            .Select(category =>
            {
                var forCategory = finalItems.Where(i => i.Category == category).ToList();
                return new ComplianceCategorySummary(
                    category.ToString(),
                    CategoryLabel(category),
                    forCategory.Count,
                    forCategory.Count(i => i.Severity == ComplianceSeverity.Overdue),
                    forCategory.Count(i => i.Severity == ComplianceSeverity.DueSoon),
                    forCategory.Count(i => i.Severity == ComplianceSeverity.Informational));
            })
            .ToList();

        var sorted = finalItems
            .OrderBy(i => (int)i.Severity)
            .ThenBy(i => i.DueDate ?? DateOnly.MaxValue)
            .ThenBy(i => i.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => (int)i.Category)
            .ToList();

        var totalCount = sorted.Count;
        var isTruncated = totalCount > ReportLimits.DisplayRowLimit;

        var rows = sorted
            .Take(ReportLimits.DisplayRowLimit)
            .Select(i => new ComplianceItemRow(
                i.EmployeeId,
                i.EmployeeName,
                i.Department,
                i.Category.ToString(),
                CategoryLabel(i.Category),
                i.Detail,
                i.DueDate,
                i.Severity.ToString(),
                i.DeepLinkUrl))
            .ToList();

        return Result.Success(new GetComplianceCentreResponse(
            rows,
            categorySummaries,
            summary,
            totalCount,
            isTruncated,
            NoActionRequired: totalCount == 0));
    }

    private static ComplianceSeverity ComputeSeverity(DateOnly? dueDate, DateOnly today)
    {
        if (dueDate is null)
            return ComplianceSeverity.Informational;

        if (dueDate < today)
            return ComplianceSeverity.Overdue;

        return dueDate <= today.AddDays(DueSoonWindowDays)
            ? ComplianceSeverity.DueSoon
            : ComplianceSeverity.Informational;
    }

    private static string CategoryLabel(ComplianceCategory category) => category switch
    {
        ComplianceCategory.ExpiringVisa => "Expiring Visas & Right to Work",
        ComplianceCategory.ExpiringCertification => "Expiring Certifications & Qualifications",
        ComplianceCategory.ExpiringOtherDocument => "Other Expiring Documents",
        ComplianceCategory.MissingRequiredDocument => "Missing Required Documents",
        ComplianceCategory.OutstandingDocumentRequest => "Outstanding Document Requests",
        ComplianceCategory.ProbationReview => "Probation Reviews Due or Overdue",
        _ => category.ToString()
    };

    private static string DocumentsDeepLink(Guid companyId, Guid employeeId)
        => $"/companies/{companyId}/employees/{employeeId}/documents";

    private static string EmployeeDeepLink(Guid companyId, Guid employeeId)
        => $"/companies/{companyId}/employees/{employeeId}/view";

    private sealed record RawItem(
        Guid EmployeeId,
        ComplianceCategory Category,
        string Detail,
        DateOnly? DueDate,
        string DeepLinkUrl);

    private sealed record EnrichedItem(
        Guid EmployeeId,
        string EmployeeName,
        string? Department,
        ComplianceCategory Category,
        string Detail,
        DateOnly? DueDate,
        ComplianceSeverity Severity,
        string DeepLinkUrl);
}
