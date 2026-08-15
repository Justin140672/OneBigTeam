using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Features.ListBackgroundJobs;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests;

public class ListBackgroundJobsHandlerTests
{
    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        var reader = new FakeBackgroundJobStatusReader();
        var handler = BuildHandler(
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"),
            reader);

        var result = await handler.HandleAsync(new ListBackgroundJobsRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Is_Null()
    {
        var reader = new FakeBackgroundJobStatusReader();
        var handler = BuildHandler(
            new FakeCurrentUser(Guid.NewGuid(), email: null),
            BuildConfiguration("admin@example.com"),
            reader);

        var result = await handler.HandleAsync(new ListBackgroundJobsRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Mapped_Job_Lists_For_AllowListed_Admin()
    {
        var scheduled = new BackgroundJobDetail("job-1", "SomeScheduledJob", "Scheduled", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow.AddMinutes(5), 0, null);
        var running = new BackgroundJobDetail("job-2", "SomeRunningJob", "Processing", null, DateTimeOffset.UtcNow, null, 0, null);
        var failed = new BackgroundJobDetail("job-3", "SomeFailedJob", "Failed", null, DateTimeOffset.UtcNow.AddMinutes(-10), null, 2, "boom");

        var reader = new FakeBackgroundJobStatusReader
        {
            ScheduledJobsToReturn = [scheduled],
            RunningJobsToReturn = [running],
            FailedJobsToReturn = [failed],
        };

        var handler = BuildHandler(
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            reader);

        var result = await handler.HandleAsync(new ListBackgroundJobsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value!;
        Assert.True(response.Available);

        var scheduledItem = Assert.Single(response.Scheduled);
        Assert.Equal("job-1", scheduledItem.JobId);
        Assert.Equal("SomeScheduledJob", scheduledItem.JobName);
        Assert.Equal("Scheduled", scheduledItem.State);

        var runningItem = Assert.Single(response.Running);
        Assert.Equal("job-2", runningItem.JobId);
        Assert.Equal("Processing", runningItem.State);

        var failedItem = Assert.Single(response.Failed);
        Assert.Equal("job-3", failedItem.JobId);
        Assert.Equal("Failed", failedItem.State);
        Assert.Equal(2, failedItem.RetryCount);
        Assert.Equal("boom", failedItem.FailureReason);
    }

    private static ListBackgroundJobsHandler BuildHandler(
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        IBackgroundJobStatusReader backgroundJobStatusReader)
    {
        return new ListBackgroundJobsHandler(currentUser, configuration, backgroundJobStatusReader);
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
