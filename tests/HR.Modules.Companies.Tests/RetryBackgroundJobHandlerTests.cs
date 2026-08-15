using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Features.RetryBackgroundJob;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class RetryBackgroundJobHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        var reader = new FakeBackgroundJobStatusReader();
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher,
            reader);

        var result = await handler.HandleAsync(
            new RetryBackgroundJobRequest { JobId = "job-1", Reason = "Investigating failure" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Job_Not_In_FailedJobs_And_Does_Not_Publish_Audit()
    {
        var reader = new FakeBackgroundJobStatusReader
        {
            FailedJobsToReturn = [],
        };
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher,
            reader);

        var result = await handler.HandleAsync(
            new RetryBackgroundJobRequest { JobId = "missing-job", Reason = "Investigating failure" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(publisher.Published);
        Assert.Null(reader.LastRetriedJobId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Success_And_Publishes_Audit_When_Retry_Succeeds()
    {
        var failedJob = new BackgroundJobDetail("job-1", "SomeFailedJob", "Failed", null, Now.AddMinutes(-10), null, 1, "boom");
        var reader = new FakeBackgroundJobStatusReader
        {
            FailedJobsToReturn = [failedJob],
            RetryResultToReturn = new BackgroundJobRetryResult(true, null),
        };
        var publisher = new CapturingAuditEventPublisher();
        var actorId = Guid.NewGuid();
        var handler = BuildHandler(
            new FakeCurrentUser(actorId, email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher,
            reader);

        var result = await handler.HandleAsync(
            new RetryBackgroundJobRequest { JobId = "job-1", Reason = "Investigating failure" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("job-1", result.Value!.JobId);
        Assert.True(result.Value.Success);
        Assert.Equal("job-1", reader.LastRetriedJobId);

        var published = Assert.Single(publisher.Published);
        var auditEvent = Assert.IsType<BackgroundJobRetriedByAdminAuditEvent>(published);
        Assert.Equal("job-1", auditEvent.JobId);
        Assert.Equal("SomeFailedJob", auditEvent.JobName);
        Assert.Equal(actorId, auditEvent.ActorUserId);
        Assert.Equal("Investigating failure", auditEvent.Reason);
        Assert.True(auditEvent.Success);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_But_Still_Publishes_Audit_When_Retry_Fails()
    {
        var failedJob = new BackgroundJobDetail("job-1", "SomeFailedJob", "Failed", null, Now.AddMinutes(-10), null, 1, "boom");
        var reader = new FakeBackgroundJobStatusReader
        {
            FailedJobsToReturn = [failedJob],
            RetryResultToReturn = new BackgroundJobRetryResult(false, "Hangfire storage unavailable"),
        };
        var publisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            publisher,
            reader);

        var result = await handler.HandleAsync(
            new RetryBackgroundJobRequest { JobId = "job-1", Reason = "Investigating failure" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        var published = Assert.Single(publisher.Published);
        var auditEvent = Assert.IsType<BackgroundJobRetriedByAdminAuditEvent>(published);
        Assert.False(auditEvent.Success);
        Assert.Equal("Hangfire storage unavailable", auditEvent.Error);
    }

    private static RetryBackgroundJobHandler BuildHandler(
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        HR.SharedKernel.IAuditEventPublisher auditEventPublisher,
        IBackgroundJobStatusReader backgroundJobStatusReader)
    {
        return new RetryBackgroundJobHandler(
            currentUser,
            configuration,
            new FakeClock(Now.UtcDateTime),
            auditEventPublisher,
            backgroundJobStatusReader);
    }

    private static IConfiguration BuildConfiguration(params string[] allowedEmails)
    {
        var builder = new ConfigurationBuilder();

        if (allowedEmails.Length > 0)
        {
            var data = allowedEmails
                .Select((email, index) => new KeyValuePair<string, string?>($"PlatformAdmin:AllowedEmails:{index}", email))
                .ToArray();
            builder.AddInMemoryCollection(data);
        }
        else
        {
            builder.AddInMemoryCollection();
        }

        return builder.Build();
    }
}
