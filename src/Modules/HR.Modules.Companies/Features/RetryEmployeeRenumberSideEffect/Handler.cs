using Hangfire;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Jobs;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.RetryEmployeeRenumberSideEffect;

/// <summary>
/// SET-08: "a failed renumber operation is visible and can be retried" — resets a Failed outbox
/// row back to Pending and re-enqueues the job. Requires "hr-settings:manage", same as the
/// settings endpoints that can trigger this side effect in the first place.
/// </summary>
internal sealed class RetryEmployeeRenumberSideEffectHandler(
    CompaniesDbContext dbContext,
    IClock clock,
    IBackgroundJobClient backgroundJobClient)
{
    public async Task<Result<RetryEmployeeRenumberSideEffectResponse>> HandleAsync(
        RetryEmployeeRenumberSideEffectRequest request,
        CancellationToken cancellationToken)
    {
        var message = await dbContext.OutboxMessages
            .SingleOrDefaultAsync(
                m => m.Id == request.OutboxMessageId && m.CompanyId == request.CompanyId,
                cancellationToken);

        if (message is null)
            return Result.Failure<RetryEmployeeRenumberSideEffectResponse>(
                Error.NotFound($"Employee renumber side effect '{request.OutboxMessageId}' was not found."));

        if (message.Status != OutboxMessage.StatusFailed)
            return Result.Failure<RetryEmployeeRenumberSideEffectResponse>(
                Error.Validation($"Cannot retry a side effect with status '{message.Status}'. Only a Failed side effect can be retried."));

        message.ResetForRetry(clock.UtcNowOffset());
        await dbContext.SaveChangesAsync(cancellationToken);

        backgroundJobClient.Enqueue<EmployeeRenumberSideEffectJob>(job => job.ProcessAsync(message.Id));

        return Result.Success(new RetryEmployeeRenumberSideEffectResponse(message.Id, message.CompanyId, message.Status));
    }
}
