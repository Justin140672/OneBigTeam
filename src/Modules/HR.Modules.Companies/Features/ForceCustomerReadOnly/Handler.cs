using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.ForceCustomerReadOnly;

/// <summary>
/// Same defense-in-depth allow-list gate as ExtendCustomerTrialHandler (see its remarks).
/// </summary>
internal sealed class ForceCustomerReadOnlyHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<ForceCustomerReadOnlyResponse>> HandleAsync(
        ForceCustomerReadOnlyRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<ForceCustomerReadOnlyResponse>(
                Error.Unauthorized("This account is not authorised to manage platform-wide customer subscriptions."));
        }

        var subscription = await dbContext.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<ForceCustomerReadOnlyResponse>(
                Error.NotFound($"No subscription record was found for company '{request.CompanyId}'."));
        }

        var now = clock.UtcNowOffset();

        var forceResult = subscription.ForceReadOnly(now);
        if (forceResult.IsFailure)
        {
            return Result.Failure<ForceCustomerReadOnlyResponse>(forceResult.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new ReadOnlyModeForcedByAdminAuditEvent(
                subscription.CompanyId, currentUser.UserId, now, request.Reason),
            cancellationToken);

        return Result.Success(new ForceCustomerReadOnlyResponse(subscription.CompanyId, subscription.AdminForcedReadOnly));
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
