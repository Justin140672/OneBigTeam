using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.OnEmployeeDepartureFinalised;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// Ticket 23 (P2): durable correlation/causation/message-id metadata on
/// <see cref="AccountDisablement"/> — extends the pattern already established for
/// HR.Modules.Employees (the reference module; see
/// HR.Modules.Employees.Tests.AuditOutboxMetadataTests) to Identity's own durable disablement
/// operation and its owning job (<see cref="AccountDisablementJob"/>).
/// </summary>
[Collection("IdentityDatabase")]
public class Ticket23AccountDisablementMetadataTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);


    [Fact]
    public void CreatePending_With_Supplied_Context_Stamps_CorrelationId_From_Context_CorrelationId_And_CausationId_From_Context_MessageId()
    {
        var context = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        var request = AccountDisablement.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, context);

        Assert.Equal(context.MessageId, request.CorrelationId);
        Assert.Equal(context.MessageId, request.CausationId);
        Assert.NotNull(request.MessageId);
        Assert.NotEqual(Guid.Empty, request.MessageId!.Value);
        Assert.NotEqual(context.MessageId, request.MessageId!.Value);
    }

    [Fact]
    public void CreatePending_With_No_Context_Leaves_Correlation_And_Causation_Null_But_Still_Mints_A_Fresh_MessageId()
    {
        var request = AccountDisablement.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.Null(request.CorrelationId);
        Assert.Null(request.CausationId);
        Assert.NotNull(request.MessageId);
        Assert.NotEqual(Guid.Empty, request.MessageId!.Value);
    }


    private static EmployeeDepartureFinalisedIntegrationEvent BuildEvent(Guid companyId, Guid employeeId) =>
        new(companyId, employeeId, DateOnly.FromDateTime(Now.Date), Now, AccessDisabled: true);

    [Fact]
    public async Task HandleAsync_With_Ambient_Context_Stamps_The_Created_AccountDisablements_Correlation_And_Causation_From_It()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var seedDb = fixture.BuildContext())
        {
            seedDb.UserProfiles.Add(UserProfile.Create(employeeId, Guid.NewGuid(), Guid.Empty, $"{Guid.NewGuid():N}@test.com", "First", "Last", Now));
            await seedDb.SaveChangesAsync();
        }

        var accessor = new ExecutionContextAccessor();
        var ambient = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);
        var jobClient = new RecordingBackgroundJobClient();

        await using var db = fixture.BuildContext();
        var handler = new Handler(db, Clock, jobClient, accessor);

        using (accessor.Push(ambient))
        {
            await handler.HandleAsync(BuildEvent(companyId, employeeId), CancellationToken.None);
        }

        var request = await db.AccountDisablements.SingleAsync(d => d.EmployeeId == employeeId);
        Assert.Equal(ambient.MessageId, request.CorrelationId);
        Assert.Equal(ambient.MessageId, request.CausationId);
    }

    [Fact]
    public async Task HandleAsync_With_No_Accessor_Leaves_The_Created_AccountDisablements_Metadata_Null_Except_MessageId()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var seedDb = fixture.BuildContext())
        {
            seedDb.UserProfiles.Add(UserProfile.Create(employeeId, Guid.NewGuid(), Guid.Empty, $"{Guid.NewGuid():N}@test.com", "First", "Last", Now));
            await seedDb.SaveChangesAsync();
        }

        var jobClient = new RecordingBackgroundJobClient();
        await using var db = fixture.BuildContext();
        var handler = new Handler(db, Clock, jobClient, executionContextAccessor: null);

        await handler.HandleAsync(BuildEvent(companyId, employeeId), CancellationToken.None);

        var request = await db.AccountDisablements.SingleAsync(d => d.EmployeeId == employeeId);
        Assert.Null(request.CorrelationId);
        Assert.Null(request.CausationId);
        Assert.NotNull(request.MessageId);
    }


    private sealed class ContextCapturingAuditEventPublisher(IExecutionContextAccessor accessor) : IAuditEventPublisher
    {
        public IExecutionContext? ObservedDuringPublish { get; private set; }

        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            ObservedDuringPublish = accessor.Current;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ProcessAsync_Restores_Persisted_CorrelationId_CausationId_And_MessageId_As_The_Ambient_Context()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var applicationUserId = employeeId;

        var executionContext = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        await using (var seedDb = fixture.BuildContext())
        {
            seedDb.UserProfiles.Add(UserProfile.Create(applicationUserId, Guid.NewGuid(), Guid.Empty, $"{Guid.NewGuid():N}@test.com", "First", "Last", Now));
            var request = AccountDisablement.CreatePending(
                Guid.NewGuid(), companyId, applicationUserId, employeeId, Now, executionContext);
            seedDb.AccountDisablements.Add(request);
            await seedDb.SaveChangesAsync();
        }

        Guid requestId;
        await using (var readDb = fixture.BuildContext())
            requestId = (await readDb.AccountDisablements.SingleAsync(d => d.EmployeeId == employeeId)).Id;

        var accessor = new ExecutionContextAccessor();
        var auditPublisher = new ContextCapturingAuditEventPublisher(accessor);
        await using var db = fixture.BuildContext();
        var job = new AccountDisablementJob(db, Clock, auditPublisher, NullLogger<AccountDisablementJob>.Instance, accessor);

        await job.ProcessAsync(requestId, companyId);

        Assert.NotNull(auditPublisher.ObservedDuringPublish);
        var restored = auditPublisher.ObservedDuringPublish!;
        Assert.Equal(executionContext.CorrelationId, restored.CorrelationId);
        Assert.Equal(executionContext.MessageId, restored.CausationId);
        Assert.Equal(ExecutionOrigin.ReconciliationJob, restored.Origin);
    }

    [Fact]
    public async Task ProcessAsync_Legacy_Row_With_Null_Metadata_Still_Restores_A_Fresh_Root_Context_Rather_Than_Throwing()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var applicationUserId = employeeId;

        await using (var seedDb = fixture.BuildContext())
        {
            seedDb.UserProfiles.Add(UserProfile.Create(applicationUserId, Guid.NewGuid(), Guid.Empty, $"{Guid.NewGuid():N}@test.com", "First", "Last", Now));
            var request = AccountDisablement.CreatePending(Guid.NewGuid(), companyId, applicationUserId, employeeId, Now);
            seedDb.AccountDisablements.Add(request);
            await seedDb.SaveChangesAsync();
        }

        Guid requestId;
        await using (var readDb = fixture.BuildContext())
            requestId = (await readDb.AccountDisablements.SingleAsync(d => d.EmployeeId == employeeId)).Id;

        var accessor = new ExecutionContextAccessor();
        var auditPublisher = new ContextCapturingAuditEventPublisher(accessor);
        await using var db = fixture.BuildContext();
        var job = new AccountDisablementJob(db, Clock, auditPublisher, NullLogger<AccountDisablementJob>.Instance, accessor);

        var exception = await Record.ExceptionAsync(() => job.ProcessAsync(requestId, companyId));

        Assert.Null(exception);
        Assert.NotNull(auditPublisher.ObservedDuringPublish);
        Assert.Equal(ExecutionOrigin.ReconciliationJob, auditPublisher.ObservedDuringPublish!.Origin);
        Assert.Null(auditPublisher.ObservedDuringPublish.CausationId);
    }
}
