using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.GetApplicationMetrics;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Tests.Features.GetApplicationMetrics;

/// <summary>
/// Same "platform:admin" allow-list gate pattern as GetSystemHealthHandlerTests /
/// ListBackgroundJobsHandlerTests — see their remarks. Uses an in-memory CompaniesDbContext (same
/// approach as GetCustomerBillingHistoryHandlerTests) plus fake IPlatformDocumentActivityReader /
/// IPlatformUserActivityReader / IBackgroundJobStatusReader.
/// </summary>
public class GetApplicationMetricsHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Not_On_AllowList()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "someone-else@example.com"),
            BuildConfiguration("admin@example.com"),
            new FakePlatformDocumentActivityReader(),
            new FakePlatformUserActivityReader(),
            new FakeBackgroundJobStatusReader());

        var result = await handler.HandleAsync(new GetApplicationMetricsRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Email_Is_Null()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: null),
            BuildConfiguration("admin@example.com"),
            new FakePlatformDocumentActivityReader(),
            new FakePlatformUserActivityReader(),
            new FakeBackgroundJobStatusReader());

        var result = await handler.HandleAsync(new GetApplicationMetricsRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_ThirtyDay_ZeroFilled_Series_When_No_Underlying_Data()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            new FakePlatformDocumentActivityReader(),
            new FakePlatformUserActivityReader(),
            new FakeBackgroundJobStatusReader());

        var result = await handler.HandleAsync(new GetApplicationMetricsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value!;

        Assert.Equal(30, response.DailySignups.Count);
        Assert.All(response.DailySignups, point => Assert.Equal(0, point.Count));

        Assert.Equal(30, response.DailyDocumentsUploaded.Count);
        Assert.All(response.DailyDocumentsUploaded, point => Assert.Equal(0, point.Count));
    }

    [Fact]
    public async Task HandleAsync_Buckets_Signups_By_Day_And_Excludes_Companies_Outside_Window()
    {
        await using var context = BuildContext();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var inWindowDayA = today.AddDays(-5);
        var inWindowDayB = today.AddDays(-1);
        var outsideWindow = today.AddDays(-40);

        // Two companies created on the same in-window day.
        context.Companies.Add(Company.Create(
            Guid.NewGuid(), "Co A1", new DateTimeOffset(inWindowDayA.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero)));
        context.Companies.Add(Company.Create(
            Guid.NewGuid(), "Co A2", new DateTimeOffset(inWindowDayA.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)));

        // One company on a different in-window day.
        context.Companies.Add(Company.Create(
            Guid.NewGuid(), "Co B", new DateTimeOffset(inWindowDayB.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero)));

        // One company outside the trailing 30-day window — must be excluded.
        context.Companies.Add(Company.Create(
            Guid.NewGuid(), "Co Outside", new DateTimeOffset(outsideWindow.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero)));

        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            new FakePlatformDocumentActivityReader(),
            new FakePlatformUserActivityReader(),
            new FakeBackgroundJobStatusReader());

        var result = await handler.HandleAsync(new GetApplicationMetricsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value!;

        Assert.Equal(30, response.DailySignups.Count);

        var dayAPoint = response.DailySignups.Single(p => p.Date == inWindowDayA);
        Assert.Equal(2, dayAPoint.Count);

        var dayBPoint = response.DailySignups.Single(p => p.Date == inWindowDayB);
        Assert.Equal(1, dayBPoint.Count);

        Assert.Equal(3, response.DailySignups.Sum(p => p.Count));
    }

    [Fact]
    public async Task HandleAsync_Is_Idempotent_Per_Calendar_Day_For_Snapshot_Row()
    {
        await using var context = BuildContext();

        var documentReader = new FakePlatformDocumentActivityReader
        {
            ActivityToReturn = new(TotalStorageBytes: 12345, DailyUploads: []),
        };
        var userReader = new FakePlatformUserActivityReader { TotalUserCountToReturn = 7 };
        var jobReader = new FakeBackgroundJobStatusReader
        {
            SummaryToReturn = new(Available: true, ServerCount: 1, Enqueued: 0, Processing: 0, Scheduled: 0, Failed: 0, Succeeded: 42, Recurring: 0),
        };

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            documentReader,
            userReader,
            jobReader);

        var first = await handler.HandleAsync(new GetApplicationMetricsRequest(), CancellationToken.None);
        var second = await handler.HandleAsync(new GetApplicationMetricsRequest(), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var snapshotCount = await context.PlatformMetricsSnapshots
            .CountAsync(s => s.SnapshotDate == today);
        Assert.Equal(1, snapshotCount);

        var response = second.Value!;
        Assert.Equal(0, response.CurrentActiveCompanies);
        Assert.Equal(7, response.CurrentActiveUsers);
        Assert.Equal(12345, response.CurrentStorageConsumedBytes);
        Assert.Equal(42, response.CurrentBackgroundJobsSucceededTotal);
    }

    [Fact]
    public async Task HandleAsync_EmailsSentTracked_Is_Always_False_With_NonEmpty_GapReason()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            new FakePlatformDocumentActivityReader(),
            new FakePlatformUserActivityReader(),
            new FakeBackgroundJobStatusReader());

        var result = await handler.HandleAsync(new GetApplicationMetricsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value!;

        Assert.False(response.EmailsSentTracked);
        Assert.False(string.IsNullOrWhiteSpace(response.EmailsSentGapReason));
    }

    [Fact]
    public async Task HandleAsync_ActiveCompaniesTrend_Reflects_Existing_Snapshots_Ordered_By_Date()
    {
        await using var context = BuildContext();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var earlierDate = today.AddDays(-3);
        var laterDate = today.AddDays(-1);

        context.PlatformMetricsSnapshots.Add(PlatformMetricsSnapshot.Create(
            Guid.NewGuid(), laterDate, Now, activeCompanies: 5, activeUsers: 10, storageConsumedBytes: 100, backgroundJobsSucceededTotal: 1));
        context.PlatformMetricsSnapshots.Add(PlatformMetricsSnapshot.Create(
            Guid.NewGuid(), earlierDate, Now, activeCompanies: 3, activeUsers: 8, storageConsumedBytes: 50, backgroundJobsSucceededTotal: 1));
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            new FakeCurrentUser(Guid.NewGuid(), email: "admin@example.com"),
            BuildConfiguration("admin@example.com"),
            new FakePlatformDocumentActivityReader(),
            new FakePlatformUserActivityReader(),
            new FakeBackgroundJobStatusReader());

        var result = await handler.HandleAsync(new GetApplicationMetricsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var trend = result.Value!.ActiveCompaniesTrend;

        // The two pre-seeded snapshots plus the one the handler itself writes for "today".
        Assert.True(trend.Count >= 2);

        var earlierPoint = trend.Single(p => p.Date == earlierDate);
        Assert.Equal(3, earlierPoint.Count);

        var laterPoint = trend.Single(p => p.Date == laterDate);
        Assert.Equal(5, laterPoint.Count);

        var orderedDates = trend.Select(p => p.Date).ToArray();
        Assert.Equal(orderedDates.OrderBy(d => d).ToArray(), orderedDates);
    }

    private static GetApplicationMetricsHandler BuildHandler(
        CompaniesDbContext context,
        HR.SharedKernel.ICurrentUser currentUser,
        IConfiguration configuration,
        FakePlatformDocumentActivityReader documentActivityReader,
        FakePlatformUserActivityReader userActivityReader,
        FakeBackgroundJobStatusReader backgroundJobStatusReader)
    {
        return new GetApplicationMetricsHandler(
            context,
            currentUser,
            configuration,
            documentActivityReader,
            userActivityReader,
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

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
