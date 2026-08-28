using HR.Infrastructure.Abstractions;

namespace HR.Modules.Identity.Features.ExportAccessReview;

internal sealed record ExportAccessReviewRequest
{
    public Guid CompanyId { get; init; }
    public ReportExportFormat Format { get; init; } = ReportExportFormat.Csv;
}
