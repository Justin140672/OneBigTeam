using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Features.GetAccessReview;
using HR.SharedKernel;

namespace HR.Modules.Identity.Features.ExportAccessReview;

internal sealed class ExportAccessReviewHandler(
    GetAccessReviewHandler accessReviewHandler,
    IReportExporter reportExporter,
    IAuditEventPublisher auditEventPublisher,
    ICurrentUser currentUser,
    IClock clock)
{
    private static readonly string[] ColumnHeaders =
        ["Employee", "Email", "Role", "Source", "Override Expires", "Expiring Soon"];

    public async Task<ExportAccessReviewResponse> HandleAsync(
        ExportAccessReviewRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var review = await accessReviewHandler.HandleAsync(
                new GetAccessReviewRequest { CompanyId = request.CompanyId }, cancellationToken);

            var rows = review.Items
                .SelectMany(item => item.Privileges.Select(p => (IReadOnlyList<string?>)new List<string?>
                {
                    item.Name,
                    item.Email,
                    p.RoleName,
                    p.Source,
                    p.OverrideExpiresAt?.ToString("yyyy-MM-dd"),
                    p.IsExpiringSoon ? "Yes" : "No",
                }))
                .ToList();

            var exportData = new ReportExportData("Access Review", ColumnHeaders, rows);
            var file = reportExporter.Export(request.Format, exportData);

            await auditEventPublisher.PublishAsync(
                new AccessReviewExportedAuditEvent(
                    request.CompanyId, request.Format.ToString(), Success: true, rows.Count,
                    FailureReason: null, currentUser.UserId, clock.UtcNowOffset()),
                cancellationToken);

            return new ExportAccessReviewResponse(file);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await auditEventPublisher.PublishAsync(
                new AccessReviewExportedAuditEvent(
                    request.CompanyId, request.Format.ToString(), Success: false, RowCount: null,
                    ex.Message, currentUser.UserId, clock.UtcNowOffset()),
                CancellationToken.None);
            throw;
        }
    }
}
