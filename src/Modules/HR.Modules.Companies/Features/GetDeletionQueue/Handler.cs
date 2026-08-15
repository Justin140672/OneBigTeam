using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.GetDeletionQueue;

/// <summary>
/// Same defense-in-depth allow-list gate as GetCustomerDashboardHandler (see its remarks).
/// Platform-wide, not scoped to a single customer — backs the /deletion-queue page.
/// </summary>
internal sealed class GetDeletionQueueHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration)
{
    public async Task<Result<GetDeletionQueueResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<GetDeletionQueueResponse>(
                Error.Unauthorized("This account is not authorised to view platform-wide customer data."));
        }

        var items = await dbContext.CustomerSubscriptions
            .AsNoTracking()
            .Where(s => s.DeletionScheduledAt != null)
            .OrderByDescending(s => s.DeletionScheduledAt)
            .Join(
                dbContext.Companies.AsNoTracking(),
                subscription => subscription.CompanyId,
                company => company.Id,
                (subscription, company) => new DeletionQueueItemDto(
                    subscription.CompanyId,
                    company.Name,
                    subscription.DeletionScheduledAt!.Value,
                    subscription.DeletionCancelledAt,
                    subscription.DeletionExecutedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new GetDeletionQueueResponse(items));
    }

    private bool IsAllowListedPlatformAdmin()
    {
        var email = currentUser.Email;
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var allowedEmails = configuration.GetSection("PlatformAdmin:AllowedEmails").Get<string[]>()
            ?? [];

        return allowedEmails.Any(allowed =>
            string.Equals(allowed, email, StringComparison.OrdinalIgnoreCase));
    }
}
