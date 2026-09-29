using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UpdateCompanySettings;
using HR.Modules.Companies.Jobs;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Companies.Tests;

/// <summary>
/// Ticket 23 (P2): durable correlation/causation/message-id metadata on Companies' own bespoke
/// <see cref="OutboxMessage"/> (NOT the shared IAuditOutboxEntry used elsewhere) — extends the
/// pattern already established for HR.Modules.Employees (the reference module; see
/// HR.Modules.Employees.Tests.AuditOutboxMetadataTests).
///
/// NOTE (scope): UpdateCompanySettingsHandler stages an OutboxMessage row with event type
/// "companies.company-settings.updated", but there is no generic background dispatcher anywhere in
/// this module that ever reads and publishes THAT particular outbox row via
/// IIntegrationEventPublisher — unlike the "employee-numbering.reformat-requested" event type,
/// which EmployeeRenumberSideEffectJob is explicitly enqueued for right after commit. This appears
/// to be a pre-existing gap unrelated to ticket 23 (flagged separately; not addressed here). These
/// tests therefore deliberately restrict themselves to what IS actually wired up: CreatePending's
/// own stamping behaviour, and EmployeeRenumberSideEffectJob's restore-context behaviour — never a
/// dispatcher for the settings-updated event type, which doesn't exist.
/// </summary>
public class Ticket23OutboxMessageMetadataTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private const string RenumberEventType = "employee-numbering.reformat-requested";

    private static CompaniesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);


    [Fact]
    public void CreatePending_With_Supplied_Context_Stamps_CorrelationId_From_Context_CorrelationId_And_CausationId_From_Context_MessageId()
    {
        var context = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        var message = OutboxMessage.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), RenumberEventType, "{}", Now, context);

        Assert.Equal(context.MessageId, message.CorrelationId);
        Assert.Equal(context.MessageId, message.CausationId);
        Assert.NotNull(message.MessageId);
        Assert.NotEqual(Guid.Empty, message.MessageId!.Value);
        Assert.NotEqual(context.MessageId, message.MessageId!.Value);
    }

    [Fact]
    public void CreatePending_With_No_Context_Leaves_Correlation_And_Causation_Null_But_Still_Mints_A_Fresh_MessageId()
    {
        var message = OutboxMessage.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), RenumberEventType, "{}", Now);

        Assert.Null(message.CorrelationId);
        Assert.Null(message.CausationId);
        Assert.NotNull(message.MessageId);
        Assert.NotEqual(Guid.Empty, message.MessageId!.Value);
    }


    [Fact]
    public async Task HandleAsync_With_Ambient_Context_Stamps_The_Created_OutboxMessages_Correlation_And_Causation_From_It()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Acme", Now);
        company.SetSettings(CompanySettings.CreateDefault(company.Id, Now), Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var accessor = new ExecutionContextAccessor();
        var ambient = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        var handler = new UpdateCompanySettingsHandler(
            context, new FakeClock(FixedUtcNow), new NoOpAuditEventPublisher(), new FakeCurrentUser(null), accessor);

        using (accessor.Push(ambient))
        {
            var result = await handler.HandleAsync(
                new UpdateCompanySettingsRequest
                {
                    CompanyId = company.Id,
                    TimeZone = "Europe/London",
                    Locale = "en-GB",
                    Version = 1,
                },
                CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        var outboxMessage = await context.OutboxMessages.SingleAsync();
        Assert.Equal(ambient.MessageId, outboxMessage.CorrelationId);
        Assert.Equal(ambient.MessageId, outboxMessage.CausationId);
    }

    [Fact]
    public async Task HandleAsync_With_No_Accessor_Leaves_The_Created_OutboxMessages_Metadata_Null_Except_MessageId()
    {
        await using var context = BuildContext();
        var company = Company.Create(Guid.NewGuid(), "Acme", Now);
        company.SetSettings(CompanySettings.CreateDefault(company.Id, Now), Now);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new UpdateCompanySettingsHandler(
            context, new FakeClock(FixedUtcNow), new NoOpAuditEventPublisher(), new FakeCurrentUser(null));

        var result = await handler.HandleAsync(
            new UpdateCompanySettingsRequest
            {
                CompanyId = company.Id,
                TimeZone = "Europe/London",
                Locale = "en-GB",
                Version = 1,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var outboxMessage = await context.OutboxMessages.SingleAsync();
        Assert.Null(outboxMessage.CorrelationId);
        Assert.Null(outboxMessage.CausationId);
        Assert.NotNull(outboxMessage.MessageId);
    }


    private sealed class ContextCapturingEmployeeRenumberingService(IExecutionContextAccessor accessor)
        : HR.Modules.Employees.Contracts.IEmployeeRenumberingService
    {
        public IExecutionContext? ObservedDuringCall { get; private set; }

        public Task RenumberAllEmployeesAsync(Guid companyId, CancellationToken cancellationToken)
        {
            ObservedDuringCall = accessor.Current;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ProcessAsync_Restores_Persisted_CorrelationId_CausationId_And_MessageId_As_The_Ambient_Context()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var executionContext = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);
        var message = OutboxMessage.CreatePending(Guid.NewGuid(), companyId, RenumberEventType, "{}", Now, executionContext);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var accessor = new ExecutionContextAccessor();
        var renumberingService = new ContextCapturingEmployeeRenumberingService(accessor);
        var job = new EmployeeRenumberSideEffectJob(
            context, renumberingService, new FakeClock(FixedUtcNow),
            NullLogger<EmployeeRenumberSideEffectJob>.Instance, accessor);

        await job.ProcessAsync(message.Id, companyId);

        Assert.NotNull(renumberingService.ObservedDuringCall);
        var restored = renumberingService.ObservedDuringCall!;
        Assert.Equal(executionContext.CorrelationId, restored.CorrelationId);
        Assert.Equal(executionContext.MessageId, restored.CausationId);
        Assert.Equal(ExecutionOrigin.ReconciliationJob, restored.Origin);
    }

    [Fact]
    public async Task ProcessAsync_Legacy_Row_With_Null_Metadata_Still_Restores_A_Fresh_Root_Context_Rather_Than_Throwing()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var message = OutboxMessage.CreatePending(Guid.NewGuid(), companyId, RenumberEventType, "{}", Now);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var accessor = new ExecutionContextAccessor();
        var renumberingService = new ContextCapturingEmployeeRenumberingService(accessor);
        var job = new EmployeeRenumberSideEffectJob(
            context, renumberingService, new FakeClock(FixedUtcNow),
            NullLogger<EmployeeRenumberSideEffectJob>.Instance, accessor);

        var exception = await Record.ExceptionAsync(() => job.ProcessAsync(message.Id, companyId));

        Assert.Null(exception);
        Assert.NotNull(renumberingService.ObservedDuringCall);
        Assert.Equal(ExecutionOrigin.ReconciliationJob, renumberingService.ObservedDuringCall!.Origin);
        Assert.Null(renumberingService.ObservedDuringCall.CausationId);
    }
}
