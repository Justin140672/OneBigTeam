using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAcknowledgementSettings;

internal sealed class UpdateSharedCompanyDocumentAcknowledgementSettingsHandler(
    DocumentsDbContext db,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result<UpdateSharedCompanyDocumentAcknowledgementSettingsResponse>> HandleAsync(
        UpdateSharedCompanyDocumentAcknowledgementSettingsRequest request,
        Guid updatedBy,
        CancellationToken cancellationToken)
    {
        var document = await db.SharedCompanyDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<UpdateSharedCompanyDocumentAcknowledgementSettingsResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        var before = new
        {
            document.RequiresAcknowledgement,
            document.AcknowledgementDueDate,
            document.AcknowledgementStatement,
        };

        var normalizedStatement = request.RequiresAcknowledgement && !string.IsNullOrWhiteSpace(request.AcknowledgementStatement)
            ? request.AcknowledgementStatement.Trim()
            : null;
        var normalizedDueDate = request.RequiresAcknowledgement ? request.AcknowledgementDueDate : null;

        var hasChanges =
            document.RequiresAcknowledgement != request.RequiresAcknowledgement ||
            document.AcknowledgementDueDate != normalizedDueDate ||
            document.AcknowledgementStatement != normalizedStatement;

        var now = clock.UtcNowOffset();

        document.SetAcknowledgementSettings(
            request.RequiresAcknowledgement,
            request.AcknowledgementDueDate,
            request.AcknowledgementStatement,
            updatedBy,
            now);

        await db.SaveChangesAsync(cancellationToken);

        if (hasChanges)
        {
            var after = new
            {
                document.RequiresAcknowledgement,
                document.AcknowledgementDueDate,
                document.AcknowledgementStatement,
            };

            await auditPublisher.PublishAsync(new SharedCompanyDocumentAcknowledgementSettingsUpdatedAuditEvent(
                document.CompanyId,
                document.Id,
                document.Title,
                before,
                after,
                updatedBy,
                now), cancellationToken);
        }

        return Result.Success(new UpdateSharedCompanyDocumentAcknowledgementSettingsResponse(
            document.Id,
            document.CompanyId,
            document.RequiresAcknowledgement,
            document.AcknowledgementDueDate,
            document.AcknowledgementStatement,
            document.UpdatedAt));
    }
}
