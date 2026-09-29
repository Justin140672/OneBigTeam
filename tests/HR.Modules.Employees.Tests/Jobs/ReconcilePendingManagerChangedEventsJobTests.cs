using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Jobs;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Employees.Tests.Jobs;

public class ReconcilePendingManagerChangedEventsJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 11, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static PendingManagerChangedEvent CreatePending(Guid companyId, Guid? publishedAt = null)
    {
        var pendingEvent = PendingManagerChangedEvent.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, Now);
        return pendingEvent;
    }

    private sealed class SelectivelyThrowingIntegrationEventPublisher(params Guid[] throwingReportEmployeeIds)
        : IIntegrationEventPublisher
    {
        private readonly HashSet<Guid> _throwing = [.. throwingReportEmployeeIds];
        public List<IIntegrationEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent
        {
            if (integrationEvent is EmployeeManagerChangedIntegrationEvent managerChanged &&
                _throwing.Contains(managerChanged.EmployeeId))
                throw new InvalidOperationException("Simulated publish failure.");

            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }

        public async Task<bool> PublishAndConfirmAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent
        {
            try
            {
                await PublishAsync(integrationEvent, cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_Is_NoOp_When_Nothing_Pending()
    {
        await using var context = BuildContext();
        var publisher = new SelectivelyThrowingIntegrationEventPublisher();
        var job = new ReconcilePendingManagerChangedEventsJob(
            context, publisher, new FakeClock(FixedUtcNow), NullLogger<ReconcilePendingManagerChangedEventsJob>.Instance);

        var exception = await Record.ExceptionAsync(() => job.ExecuteAsync());

        Assert.Null(exception);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Publishes_Only_Records_With_Null_PublishedAt()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var pendingRecord = CreatePending(companyId);
        var alreadyPublishedRecord = CreatePending(companyId);
        alreadyPublishedRecord.MarkPublished(Now.AddMinutes(-5));

        context.PendingManagerChangedEvents.AddRange(pendingRecord, alreadyPublishedRecord);
        await context.SaveChangesAsync();

        var publisher = new SelectivelyThrowingIntegrationEventPublisher();
        var job = new ReconcilePendingManagerChangedEventsJob(
            context, publisher, new FakeClock(FixedUtcNow), NullLogger<ReconcilePendingManagerChangedEventsJob>.Instance);

        await job.ExecuteAsync();

        var published = Assert.Single(publisher.Published.OfType<EmployeeManagerChangedIntegrationEvent>());
        Assert.Equal(pendingRecord.ReportEmployeeId, published.EmployeeId);

        var reloadedPending = await context.PendingManagerChangedEvents.SingleAsync(e => e.Id == pendingRecord.Id);
        Assert.NotNull(reloadedPending.PublishedAt);

        var reloadedAlreadyPublished =
            await context.PendingManagerChangedEvents.SingleAsync(e => e.Id == alreadyPublishedRecord.Id);
        Assert.Equal(Now.AddMinutes(-5), reloadedAlreadyPublished.PublishedAt);
    }

    [Fact]
    public async Task ExecuteAsync_One_Records_Publish_Failure_Does_Not_Stop_The_Others()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var failingRecord = CreatePending(companyId);
        var healthyRecord = CreatePending(companyId);

        context.PendingManagerChangedEvents.AddRange(failingRecord, healthyRecord);
        await context.SaveChangesAsync();

        var publisher = new SelectivelyThrowingIntegrationEventPublisher(failingRecord.ReportEmployeeId);
        var job = new ReconcilePendingManagerChangedEventsJob(
            context, publisher, new FakeClock(FixedUtcNow), NullLogger<ReconcilePendingManagerChangedEventsJob>.Instance);

        var exception = await Record.ExceptionAsync(() => job.ExecuteAsync());

        Assert.Null(exception);

        var published = Assert.Single(publisher.Published.OfType<EmployeeManagerChangedIntegrationEvent>());
        Assert.Equal(healthyRecord.ReportEmployeeId, published.EmployeeId);

        var reloadedFailing = await context.PendingManagerChangedEvents.SingleAsync(e => e.Id == failingRecord.Id);
        Assert.Null(reloadedFailing.PublishedAt);

        var reloadedHealthy = await context.PendingManagerChangedEvents.SingleAsync(e => e.Id == healthyRecord.Id);
        Assert.NotNull(reloadedHealthy.PublishedAt);
    }

    [Fact]
    public async Task ExecuteAsync_Logs_Error_With_Entity_Ids_When_A_Publish_Fails()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var failingRecord = CreatePending(companyId);
        context.PendingManagerChangedEvents.Add(failingRecord);
        await context.SaveChangesAsync();

        var publisher = new SelectivelyThrowingIntegrationEventPublisher(failingRecord.ReportEmployeeId);
        var logger = new ListLogger<ReconcilePendingManagerChangedEventsJob>();
        var job = new ReconcilePendingManagerChangedEventsJob(context, publisher, new FakeClock(FixedUtcNow), logger);

        await job.ExecuteAsync();

        Assert.Contains(logger.Messages, m =>
            m.Contains(failingRecord.Id.ToString()) &&
            m.Contains(failingRecord.ReportEmployeeId.ToString()) &&
            m.Contains(failingRecord.CompanyId.ToString()) &&
            m.Contains(failingRecord.LeavingProcessId.ToString()));
    }
}
