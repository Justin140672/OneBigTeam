using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAudience;

internal sealed class UpdateSharedCompanyDocumentAudienceHandler(
    DocumentsDbContext db,
    SharedCompanyDocumentAudienceRuleBuilder audienceRuleBuilder,
    SharedCompanyDocumentAudienceMatcher audienceMatcher,
    SharedCompanyDocumentAudienceDescriber audienceDescriber,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result<UpdateSharedCompanyDocumentAudienceResponse>> HandleAsync(
        UpdateSharedCompanyDocumentAudienceRequest request,
        Guid updatedBy,
        CancellationToken cancellationToken)
    {
        var document = await db.SharedCompanyDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<UpdateSharedCompanyDocumentAudienceResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        var ruleBuildResult = await audienceRuleBuilder.BuildAsync(
            request.CompanyId,
            document.Id,
            request.AudienceDepartmentIds,
            request.AudienceLocationIds,
            request.AudiencePositionProfileIds,
            request.AudienceEmployeeIds,
            cancellationToken);

        if (ruleBuildResult.IsFailure)
            return Result.Failure<UpdateSharedCompanyDocumentAudienceResponse>(ruleBuildResult.Error);

        var (beforeDepartmentIds, beforeLocationIds, beforePositionProfileIds, beforeEmployeeIds) =
            await audienceMatcher.GetRuleTargetsByTypeAsync(document.Id, cancellationToken);

        var hasChanges =
            !beforeDepartmentIds.ToHashSet().SetEquals(request.AudienceDepartmentIds) ||
            !beforeLocationIds.ToHashSet().SetEquals(request.AudienceLocationIds) ||
            !beforePositionProfileIds.ToHashSet().SetEquals(request.AudiencePositionProfileIds) ||
            !beforeEmployeeIds.ToHashSet().SetEquals(request.AudienceEmployeeIds);

        var now = clock.UtcNowOffset();

        if (hasChanges)
        {
            var beforeDescription = await audienceDescriber.DescribeAsync(
                request.CompanyId, beforeDepartmentIds, beforeLocationIds, beforePositionProfileIds, beforeEmployeeIds, cancellationToken);

            var existingRules = await db.SharedCompanyDocumentAudienceRules
                .Where(r => r.SharedCompanyDocumentId == document.Id)
                .ToListAsync(cancellationToken);
            db.SharedCompanyDocumentAudienceRules.RemoveRange(existingRules);
            db.SharedCompanyDocumentAudienceRules.AddRange(ruleBuildResult.Value!);

            document.Touch(updatedBy, now);
            await db.SaveChangesAsync(cancellationToken);

            var afterDescription = await audienceDescriber.DescribeAsync(
                request.CompanyId,
                request.AudienceDepartmentIds, request.AudienceLocationIds,
                request.AudiencePositionProfileIds, request.AudienceEmployeeIds,
                cancellationToken);

            await auditPublisher.PublishAsync(new SharedCompanyDocumentAudienceUpdatedAuditEvent(
                document.CompanyId,
                document.Id,
                document.Title,
                beforeDescription,
                afterDescription,
                updatedBy,
                now), cancellationToken);

            return Result.Success(new UpdateSharedCompanyDocumentAudienceResponse(
                document.Id,
                document.CompanyId,
                request.AudienceDepartmentIds,
                request.AudienceLocationIds,
                request.AudiencePositionProfileIds,
                request.AudienceEmployeeIds,
                afterDescription));
        }

        var description = await audienceDescriber.DescribeAsync(
            request.CompanyId,
            request.AudienceDepartmentIds, request.AudienceLocationIds,
            request.AudiencePositionProfileIds, request.AudienceEmployeeIds,
            cancellationToken);

        return Result.Success(new UpdateSharedCompanyDocumentAudienceResponse(
            document.Id,
            document.CompanyId,
            request.AudienceDepartmentIds,
            request.AudienceLocationIds,
            request.AudiencePositionProfileIds,
            request.AudienceEmployeeIds,
            description));
    }
}
