using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.ScheduleCustomerDeletion;

/// <summary>
/// Same defense-in-depth allow-list gate as ExtendCustomerTrialHandler (see its remarks). Part of
/// the Permanent Deletion Queue (Customer Lifecycle epic) — schedules a countdown after which the
/// company becomes eligible for deletion execution (see ExecuteCustomerDeletionHandler for the
/// scope line on what "execute" actually does).
/// </summary>
internal sealed class ScheduleCustomerDeletionHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    /// <summary>
    /// Default countdown length in days when the caller does not specify one. No existing
    /// configuration key covers this, and a hardcoded sensible default matches the pattern used for
    /// other admin-tunable-but-rarely-changed constants in this module (e.g. the 20-minute support
    /// session lifetime).
    /// </summary>
    public const int DefaultCountdownDays = 30;

    public async Task<Result<ScheduleCustomerDeletionResponse>> HandleAsync(
        ScheduleCustomerDeletionRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<ScheduleCustomerDeletionResponse>(
                Error.Unauthorized("This account is not authorised to manage platform-wide customer subscriptions."));
        }

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<ScheduleCustomerDeletionResponse>(
                Error.NotFound($"No subscription record was found for company '{request.CompanyId}'."));
        }

        var now = clock.UtcNowOffset();
        var countdownDays = request.CountdownDays ?? DefaultCountdownDays;
        var scheduledFor = now.AddDays(countdownDays);

        var scheduleResult = subscription.ScheduleDeletion(currentUser.UserId, scheduledFor, now);
        if (scheduleResult.IsFailure)
        {
            return Result.Failure<ScheduleCustomerDeletionResponse>(scheduleResult.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new CustomerDeletionScheduledAuditEvent(
                subscription.CompanyId, currentUser.UserId, now, scheduledFor, request.Reason),
            cancellationToken);

        return Result.Success(new ScheduleCustomerDeletionResponse(
            subscription.CompanyId, subscription.DeletionScheduledAt!.Value));
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
