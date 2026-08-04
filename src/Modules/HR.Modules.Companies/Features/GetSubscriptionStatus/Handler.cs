using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Companies.Features.GetSubscriptionStatus;

internal sealed class GetSubscriptionStatusHandler(
    ISubscriptionStatusReader subscriptionStatusReader,
    ICurrentTenant currentTenant)
{
    public async Task<Result<GetSubscriptionStatusResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is null || !Guid.TryParse(currentTenant.TenantId, out var companyId))
        {
            return Result.Failure<GetSubscriptionStatusResponse>(
                Error.Unauthorized("No company context could be resolved for the current user."));
        }

        var snapshot = await subscriptionStatusReader.GetStatusAsync(companyId, cancellationToken);

        return Result.Success(new GetSubscriptionStatusResponse(
            snapshot.Status,
            snapshot.IsReadOnly,
            snapshot.TrialDaysRemaining));
    }
}
