using HR.Modules.Documents.Services;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Documents.Features.ApproveProfilePhoto;

internal sealed class ApproveProfilePhotoHandler(
    ProfilePhotoReviewer reviewer,
    ITaskCompleter taskCompleter)
{
    public async Task<Result<ApproveProfilePhotoResponse>> HandleAsync(
        ApproveProfilePhotoRequest request,
        Guid reviewerId,
        CancellationToken cancellationToken)
    {
        var (result, pendingPhotoId) = await reviewer.ApproveAsync(
            request.CompanyId, request.EmployeeId, reviewerId, request.IdempotencyKey, cancellationToken);

        if (result.IsFailure)
            return result;

        // pendingPhotoId is null when this call was answered from an idempotency replay (the
        // original attempt already completed the linked task) — nothing further to do here.
        if (pendingPhotoId is { } id)
        {
            await taskCompleter.CompleteBySourceEntityAsync(
                request.CompanyId, id, TaskSource.Document, TaskActionType.Review,
                completedBy: reviewerId, cancellationToken);
        }

        return result;
    }
}
