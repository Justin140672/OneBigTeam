using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.GetCustomerDashboard;

/// <summary>
/// No first-class "platform administrator" identity model exists yet in this codebase (see
/// IdentityModule.AddRolePolicies' "platform:admin" policy remarks) — every existing role is
/// scoped to a single company via RoleAssignment, which cannot express "sees every company".
/// Until that model exists, this handler is a second, defense-in-depth gate behind the
/// "platform:admin" endpoint policy (which only proves the caller is *some* authenticated
/// Supabase user): the caller's email must additionally appear in the
/// "PlatformAdmin:AllowedEmails" configuration allow-list, or the request is rejected. This
/// deliberately conservative approach avoids exposing cross-tenant customer data to every
/// authenticated user in the system while a proper platform-admin identity model is designed.
/// </summary>
internal sealed class GetCustomerDashboardHandler(
    CompaniesDbContext dbContext,
    HR.SharedKernel.ICurrentUser currentUser,
    IConfiguration configuration)
{
    private readonly CompaniesDbContext _dbContext = dbContext;

    public async Task<Result<GetCustomerDashboardResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<GetCustomerDashboardResponse>(
                Error.Unauthorized("This account is not authorised to view platform-wide customer data."));
        }

        var totalCustomers = await _dbContext.Companies
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var activeCustomers = await _dbContext.Companies
            .AsNoTracking()
            .CountAsync(c => c.Status == Domain.CompanyStatus.Active, cancellationToken);

        var trialCustomers = await _dbContext.CustomerSubscriptions
            .AsNoTracking()
            .CountAsync(s => s.Status == SubscriptionStatus.Trial, cancellationToken);

        var readOnlyCustomers = await _dbContext.CustomerSubscriptions
            .AsNoTracking()
            .CountAsync(s => s.Status == SubscriptionStatus.TrialExpired, cancellationToken);

        var cancelledSubscriptions = await _dbContext.CustomerSubscriptions
            .AsNoTracking()
            .CountAsync(s => s.Status == SubscriptionStatus.Canceled, cancellationToken);

        var recentRegistrations = await _dbContext.Companies
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Take(10)
            .Select(c => new CustomerDashboardRegistrationDto(c.Id, c.Name, c.CreatedAt))
            .ToListAsync(cancellationToken);

        var recentSubscriptionChanges = await _dbContext.CustomerSubscriptions
            .AsNoTracking()
            .OrderByDescending(s => s.UpdatedAt)
            .Take(10)
            .Join(
                _dbContext.Companies.AsNoTracking(),
                subscription => subscription.CompanyId,
                company => company.Id,
                (subscription, company) => new CustomerDashboardSubscriptionChangeDto(
                    subscription.CompanyId,
                    company.Name,
                    subscription.Status.ToString(),
                    subscription.UpdatedAt))
            .ToListAsync(cancellationToken);

        // Permanent Deletion Queue (Customer Lifecycle epic) — a company counts as "pending" while
        // it has an active, uncancelled, unexecuted deletion countdown (see
        // CustomerSubscription.HasPendingDeletion).
        var pendingPermanentDeletions = await _dbContext.CustomerSubscriptions
            .AsNoTracking()
            .CountAsync(
                s => s.DeletionScheduledAt != null
                    && s.DeletionCancelledAt == null
                    && s.DeletionExecutedAt == null,
                cancellationToken);

        return Result.Success(new GetCustomerDashboardResponse(
            totalCustomers,
            activeCustomers,
            trialCustomers,
            readOnlyCustomers,
            cancelledSubscriptions,
            pendingPermanentDeletions,
            recentRegistrations,
            recentSubscriptionChanges));
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
