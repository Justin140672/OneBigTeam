using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.RetryBackgroundJob;

/// <summary>
/// Requeues a failed background job (Background Jobs epic, Job Monitoring story). Same
/// defense-in-depth allow-list gate as ForceCustomerReadOnlyHandler (see its remarks), and the same
/// "audit every administrative intervention" convention as the other Subscription Management
/// actions in this module.
/// </summary>
internal sealed class RetryBackgroundJobHandler(
    ICurrentUser currentUser,
    IConfiguration configuration,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    IBackgroundJobStatusReader backgroundJobStatusReader)
{
    public async Task<Result<RetryBackgroundJobResponse>> HandleAsync(
        RetryBackgroundJobRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<RetryBackgroundJobResponse>(
                Error.Unauthorized("This account is not authorised to manage platform-wide background jobs."));
        }

        var failedJob = backgroundJobStatusReader.GetFailedJobs()
            .FirstOrDefault(j => string.Equals(j.JobId, request.JobId, StringComparison.Ordinal));

        if (failedJob is null)
        {
            return Result.Failure<RetryBackgroundJobResponse>(
                Error.NotFound($"No failed job with id '{request.JobId}' was found."));
        }

        var retryResult = backgroundJobStatusReader.RetryJob(request.JobId);

        await auditEventPublisher.PublishAsync(
            new BackgroundJobRetriedByAdminAuditEvent(
                request.JobId,
                failedJob.JobName,
                currentUser.UserId,
                clock.UtcNowOffset(),
                request.Reason,
                retryResult.Success,
                retryResult.Error),
            cancellationToken);

        if (!retryResult.Success)
        {
            return Result.Failure<RetryBackgroundJobResponse>(
                Error.Conflict(retryResult.Error ?? "The job could not be retried."));
        }

        return Result.Success(new RetryBackgroundJobResponse(request.JobId, true));
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
