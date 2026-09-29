using HR.Modules.Documents.Services;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Documents.Features.RejectProfilePhoto;

internal sealed class RejectProfilePhotoHandler(
    ProfilePhotoReviewer reviewer,
    ITaskCompleter taskCompleter)
{
    public async Task<Result<RejectProfilePhotoResponse>> HandleAsync(
        RejectProfilePhotoRequest request,
        Guid reviewerId,
        CancellationToken cancellationToken)
    {
        var (result, pendingPhotoId) = await reviewer.RejectAsync(
            request.CompanyId, request.EmployeeId, reviewerId, request.RejectionReason,
            request.IdempotencyKey, cancellationToken);

        if (result.IsFailure)
            return result;

        if (pendingPhotoId is { } id)
        {
            await taskCompleter.CompleteBySourceEntityAsync(
                request.CompanyId, id, TaskSource.Document, TaskActionType.Review,
                completedBy: reviewerId, cancellationToken);
        }

        return result;
    }
}
