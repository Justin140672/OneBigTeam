using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.SetCustomerOriginalStatus;

internal sealed class SetCustomerOriginalStatusHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<SetCustomerOriginalStatusResponse>> HandleAsync(
        Guid companyId,
        SetCustomerOriginalStatusRequest request,
        CancellationToken cancellationToken)
    {
        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<SetCustomerOriginalStatusResponse>(
                Error.NotFound($"No subscription record was found for company '{companyId}'."));
        }

        if (subscription.Version != request.ExpectedVersion)
        {
            return Result.Failure<SetCustomerOriginalStatusResponse>(
                Error.Conflict($"Optimistic concurrency conflict. Expected version {request.ExpectedVersion}, but found version {subscription.Version}."));
        }

        var oldIsOriginalCustomer = subscription.IsOriginalCustomer;
        var now = clock.UtcNowOffset();

        var result = subscription.SetOriginalCustomerStatus(request.IsOriginalCustomer, currentUser.UserId, now);
        if (result.IsFailure)
        {
            return Result.Failure<SetCustomerOriginalStatusResponse>(result.Error);
        }

        var response = new SetCustomerOriginalStatusResponse(
            subscription.CompanyId,
            subscription.IsOriginalCustomer,
            subscription.Version);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<SetCustomerOriginalStatusResponse>(
                Error.Conflict("The subscription was modified by another request. Please reload and try again."));
        }

        await auditEventPublisher.PublishAsync(
            new CustomerClassificationUpdatedAuditEvent(
                subscription.CompanyId,
                currentUser.UserId,
                now,
                oldIsOriginalCustomer,
                subscription.IsOriginalCustomer),
            cancellationToken);

        return Result.Success(response);
    }
}
