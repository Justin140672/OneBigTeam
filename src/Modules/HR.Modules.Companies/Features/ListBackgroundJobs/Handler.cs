using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.ListBackgroundJobs;

/// <summary>
/// Platform-wide background job monitoring for the Admin Portal (Background Jobs epic, Job
/// Monitoring story). Same defense-in-depth allow-list gate as GetCustomerSupportViewHandler (see
/// its remarks) — no first-class platform-administrator identity model exists yet.
///
/// Deliberately reads through IBackgroundJobStatusReader (Infrastructure-owned, wraps Hangfire's
/// IMonitoringApi) rather than taking a direct Hangfire dependency in this module, same "module
/// consumes an Infrastructure-owned abstraction" shape as the existing GetStatus() usage.
/// </summary>
internal sealed class ListBackgroundJobsHandler(
    ICurrentUser currentUser,
    IConfiguration configuration,
    IBackgroundJobStatusReader backgroundJobStatusReader)
{
    public Task<Result<ListBackgroundJobsResponse>> HandleAsync(
        ListBackgroundJobsRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Task.FromResult(Result.Failure<ListBackgroundJobsResponse>(
                Error.Unauthorized("This account is not authorised to view platform-wide job data.")));
        }

        var status = backgroundJobStatusReader.GetStatus();

        var response = new ListBackgroundJobsResponse(
            status.Available,
            Map(backgroundJobStatusReader.GetScheduledJobs()),
            Map(backgroundJobStatusReader.GetRunningJobs()),
            Map(backgroundJobStatusReader.GetFailedJobs()));

        return Task.FromResult(Result.Success(response));
    }

    private static IReadOnlyList<BackgroundJobItem> Map(IReadOnlyList<BackgroundJobDetail> details) =>
        details
            .Select(d => new BackgroundJobItem(
                d.JobId, d.JobName, d.State, d.ScheduledAt, d.LastExecutedAt, d.NextExecutionAt,
                d.RetryCount, d.FailureReason))
            .ToList();

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
