using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using HR.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Employees.Tests;

/// <summary>
/// Ticket 23 (P2): durable outbox metadata (CorrelationId/CausationId/MessageId) staged by
/// <see cref="DbSetAuditOutboxExtensions.EnqueueIntegrationOutbox{TEntry,TIntegrationEvent}"/> and
/// restored by <see cref="DbSetAuditOutboxExtensions.DispatchPendingAsync{TEntry}"/>. Employees is
/// the reference-implementation module for this ticket, so its own <see cref="AuditOutboxEntry"/>
/// is used directly (same pattern as <c>CreateEmployeeIdempotencyTests</c> — EF Core InMemory
/// against <see cref="EmployeesDbContext"/>, no real database required).
/// </summary>
public class AuditOutboxMetadataTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    private sealed record TestOutboxIntegrationEvent(Guid Marker) : IIntegrationEvent;

    private sealed class ContextCapturingIntegrationEventPublisher(IExecutionContextAccessor accessor) : IIntegrationEventPublisher
    {
        public IExecutionContext? ObservedDuringLastPublish { get; private set; }

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent
        {
            ObservedDuringLastPublish = accessor.Current;
            return Task.CompletedTask;
        }

        public Task<bool> PublishAndConfirmAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent
        {
            ObservedDuringLastPublish = accessor.Current;
            return Task.FromResult(true);
        }
    }

    private sealed class NoOpAuditEventPublisher : IAuditEventPublisher
    {
        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static EmployeesDbContext BuildContext(string dbName) =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);


    [Fact]
    public async Task EnqueueIntegrationOutbox_With_Ambient_Context_Stamps_CorrelationId_And_CausationId_From_It()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var context = BuildContext(dbName);
        var accessor = new ExecutionContextAccessor();
        var ambient = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);
        var companyId = Guid.NewGuid();

        using (accessor.Push(ambient))
        {
            context.AuditOutboxEntries.EnqueueIntegrationOutbox(
                new TestOutboxIntegrationEvent(Guid.NewGuid()), companyId, Now, accessor);
            await context.SaveChangesAsync();
        }

        var entry = await context.AuditOutboxEntries.SingleAsync();
        Assert.Equal(ambient.MessageId, entry.CorrelationId);
        Assert.Equal(ambient.MessageId, entry.CausationId);
    }

    [Fact]
    public async Task EnqueueIntegrationOutbox_Always_Mints_A_Fresh_MessageId_Independent_Of_Ambient()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var context = BuildContext(dbName);
        var accessor = new ExecutionContextAccessor();
        var ambient = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);
        var companyId = Guid.NewGuid();

        using (accessor.Push(ambient))
        {
            context.AuditOutboxEntries.EnqueueIntegrationOutbox(
                new TestOutboxIntegrationEvent(Guid.NewGuid()), companyId, Now, accessor);
            await context.SaveChangesAsync();
        }

        var entry = await context.AuditOutboxEntries.SingleAsync();
        Assert.NotNull(entry.MessageId);
        Assert.NotEqual(ambient.MessageId, entry.MessageId);
        Assert.NotEqual(Guid.Empty, entry.MessageId!.Value);
    }

    [Fact]
    public async Task EnqueueIntegrationOutbox_With_No_Accessor_Leaves_All_Metadata_Null()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var context = BuildContext(dbName);
        var companyId = Guid.NewGuid();

        context.AuditOutboxEntries.EnqueueIntegrationOutbox(
            new TestOutboxIntegrationEvent(Guid.NewGuid()), companyId, Now, executionContextAccessor: null);
        await context.SaveChangesAsync();

        var entry = await context.AuditOutboxEntries.SingleAsync();
        Assert.Null(entry.CorrelationId);
        Assert.Null(entry.CausationId);
        Assert.NotNull(entry.MessageId);
        Assert.NotEqual(Guid.Empty, entry.MessageId!.Value);
    }

    [Fact]
    public async Task EnqueueIntegrationOutbox_With_Accessor_But_Nothing_Ambient_Leaves_Correlation_And_Causation_Null()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var context = BuildContext(dbName);
        var accessor = new ExecutionContextAccessor();
        var companyId = Guid.NewGuid();

        context.AuditOutboxEntries.EnqueueIntegrationOutbox(
            new TestOutboxIntegrationEvent(Guid.NewGuid()), companyId, Now, accessor);
        await context.SaveChangesAsync();

        var entry = await context.AuditOutboxEntries.SingleAsync();
        Assert.Null(entry.CorrelationId);
        Assert.Null(entry.CausationId);
        Assert.NotNull(entry.MessageId);
        Assert.NotEqual(Guid.Empty, entry.MessageId!.Value);
    }


    [Fact]
    public async Task DispatchPendingAsync_Restores_Persisted_CorrelationId_CausationId_And_MessageId_Across_A_Simulated_Restart()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        Guid stagedCorrelationId;
        Guid stagedCausationId;
        Guid stagedMessageId;

        await using (var firstProcessContext = BuildContext(dbName))
        {
            var firstAccessor = new ExecutionContextAccessor();
            var ambient = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

            using (firstAccessor.Push(ambient))
            {
                firstProcessContext.AuditOutboxEntries.EnqueueIntegrationOutbox(
                    new TestOutboxIntegrationEvent(Guid.NewGuid()), companyId, Now, firstAccessor);
                await firstProcessContext.SaveChangesAsync();
            }

            var staged = await firstProcessContext.AuditOutboxEntries.SingleAsync();
            stagedCorrelationId = staged.CorrelationId!.Value;
            stagedCausationId = staged.CausationId!.Value;
            stagedMessageId = staged.MessageId!.Value;
        }

        await using var secondProcessContext = BuildContext(dbName);
        var secondAccessor = new ExecutionContextAccessor();
        var capturingPublisher = new ContextCapturingIntegrationEventPublisher(secondAccessor);

        await secondProcessContext.DispatchPendingAsync(
            secondProcessContext.AuditOutboxEntries,
            new NoOpAuditEventPublisher(),
            Now,
            batchSize: 10,
            NullLogger.Instance,
            CancellationToken.None,
            integrationPublisher: capturingPublisher,
            executionContextAccessor: secondAccessor);

        Assert.NotNull(capturingPublisher.ObservedDuringLastPublish);
        var restored = capturingPublisher.ObservedDuringLastPublish!;
        Assert.Equal(stagedCorrelationId.ToString("D"), restored.CorrelationId);
        Assert.Equal(stagedCausationId, restored.CausationId);
        Assert.Equal(stagedMessageId, restored.MessageId);
        Assert.Equal(ExecutionOrigin.ReconciliationJob, restored.Origin);

        var dispatched = await secondProcessContext.AuditOutboxEntries.SingleAsync();
        Assert.NotNull(dispatched.DispatchedAt);
    }

    [Fact]
    public async Task DispatchPendingAsync_Legacy_Row_With_Null_Metadata_Still_Dispatches_Without_Exception()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var context = BuildContext(dbName);

        context.AuditOutboxEntries.Add(new AuditOutboxEntry
        {
            Id = Guid.NewGuid(),
            Channel = OutboxChannel.Integration,
            EventTypeName = typeof(TestOutboxIntegrationEvent).AssemblyQualifiedName!,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new TestOutboxIntegrationEvent(Guid.NewGuid())),
            CompanyId = Guid.NewGuid(),
            CreatedAt = Now,
            CorrelationId = null,
            CausationId = null,
            MessageId = null,
        });
        await context.SaveChangesAsync();

        var accessor = new ExecutionContextAccessor();
        var capturingPublisher = new ContextCapturingIntegrationEventPublisher(accessor);

        var exception = await Record.ExceptionAsync(() => context.DispatchPendingAsync(
            context.AuditOutboxEntries,
            new NoOpAuditEventPublisher(),
            Now,
            batchSize: 10,
            NullLogger.Instance,
            CancellationToken.None,
            integrationPublisher: capturingPublisher,
            executionContextAccessor: accessor));

        Assert.Null(exception);

        var dispatched = await context.AuditOutboxEntries.SingleAsync();
        Assert.NotNull(dispatched.DispatchedAt);

        Assert.NotNull(capturingPublisher.ObservedDuringLastPublish);
        Assert.Equal(ExecutionOrigin.ReconciliationJob, capturingPublisher.ObservedDuringLastPublish!.Origin);
        Assert.Null(capturingPublisher.ObservedDuringLastPublish.CausationId);
    }
}
