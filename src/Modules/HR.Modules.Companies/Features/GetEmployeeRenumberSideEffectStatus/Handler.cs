using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.GetEmployeeRenumberSideEffectStatus;

internal sealed class GetEmployeeRenumberSideEffectStatusHandler(CompaniesDbContext dbContext)
{
    public async Task<Result<GetEmployeeRenumberSideEffectStatusResponse>> HandleAsync(
        GetEmployeeRenumberSideEffectStatusRequest request,
        CancellationToken cancellationToken)
    {
        var message = await dbContext.OutboxMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                m => m.Id == request.OutboxMessageId && m.CompanyId == request.CompanyId,
                cancellationToken);

        if (message is null)
            return Result.Failure<GetEmployeeRenumberSideEffectStatusResponse>(
                Error.NotFound($"Employee renumber side effect '{request.OutboxMessageId}' was not found."));

        return Result.Success(new GetEmployeeRenumberSideEffectStatusResponse(
            message.Id,
            message.CompanyId,
            message.Status,
            message.AttemptCount,
            message.LastAttemptAt,
            message.ErrorMessage,
            message.CreatedAt,
            message.ProcessedAt,
            message.FailedAt));
    }
}
