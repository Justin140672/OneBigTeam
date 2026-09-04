using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Jobs;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Companies.Tests;

public class EmployeeRenumberSideEffectJobTests
{
    private const string EventType = "employee-numbering.reformat-requested";

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }

    private static OutboxMessage SeedPending(CompaniesDbContext context, Guid companyId, DateTimeOffset createdAt)
    {
        var message = OutboxMessage.CreatePending(Guid.NewGuid(), companyId, EventType, "{}", createdAt);
        context.OutboxMessages.Add(message);
        context.SaveChanges();
        return message;
    }

    [Fact]
    public async Task ProcessAsync_Happy_Path_Marks_Processed_And_Calls_RenumberingService_Once()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc));
        var message = SeedPending(context, companyId, createdAt);

        var renumberingService = new CapturingEmployeeRenumberingService();
        var job = new EmployeeRenumberSideEffectJob(
            context, renumberingService, new FakeClock(new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc)),
            NullLogger<EmployeeRenumberSideEffectJob>.Instance);

        await job.ProcessAsync(message.Id, companyId);

        var reloaded = await context.OutboxMessages.SingleAsync(m => m.Id == message.Id);
        Assert.Equal(OutboxMessage.StatusProcessed, reloaded.Status);
        Assert.NotNull(reloaded.ProcessedAt);
        Assert.Equal(1, renumberingService.CallCount);
        Assert.Equal(companyId, renumberingService.Calls.Single());
    }

    [Fact]
    public async Task ProcessAsync_NoOps_When_Already_Processed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc));
        var message = SeedPending(context, companyId, createdAt);
        message.MarkProcessing(createdAt.AddMinutes(1));
        message.MarkProcessed(createdAt.AddMinutes(2));
        await context.SaveChangesAsync();

        var renumberingService = new CapturingEmployeeRenumberingService();
        var job = new EmployeeRenumberSideEffectJob(
            context, renumberingService, new FakeClock(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc)),
            NullLogger<EmployeeRenumberSideEffectJob>.Instance);

        await job.ProcessAsync(message.Id, companyId);

        Assert.Equal(0, renumberingService.CallCount);
        var reloaded = await context.OutboxMessages.SingleAsync(m => m.Id == message.Id);
        Assert.Equal(OutboxMessage.StatusProcessed, reloaded.Status);
        Assert.Equal(createdAt.AddMinutes(2), reloaded.ProcessedAt);
    }

    [Fact]
    public async Task ProcessAsync_Does_Not_Throw_When_Outbox_Message_Is_Missing()
    {
        await using var context = BuildContext();
        var renumberingService = new CapturingEmployeeRenumberingService();
        var job = new EmployeeRenumberSideEffectJob(
            context, renumberingService, new FakeClock(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc)),
            NullLogger<EmployeeRenumberSideEffectJob>.Instance);

        var exception = await Record.ExceptionAsync(() => job.ProcessAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Null(exception);
        Assert.Equal(0, renumberingService.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_Transient_Failure_Increments_AttemptCount_Leaves_NonFinal_And_Rethrows()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc));
        var message = SeedPending(context, companyId, createdAt);

        var renumberingService = new ThrowingEmployeeRenumberingService();
        var job = new EmployeeRenumberSideEffectJob(
            context, renumberingService, new FakeClock(new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc)),
            NullLogger<EmployeeRenumberSideEffectJob>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(message.Id, companyId));

        var reloaded = await context.OutboxMessages.SingleAsync(m => m.Id == message.Id);
        // Below MaxAttempts (4) â€” MarkProcessing bumped AttemptCount to 1, but the row is NOT
        // marked Failed yet since this isn't the final attempt.
        Assert.Equal(1, reloaded.AttemptCount);
        Assert.Equal(OutboxMessage.StatusProcessing, reloaded.Status);
        Assert.Null(reloaded.FailedAt);
        Assert.Null(reloaded.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_Final_Attempt_Failure_Marks_Failed_With_ErrorMessage_And_Rethrows()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc));
        var message = SeedPending(context, companyId, createdAt);

        var renumberingService = new ThrowingEmployeeRenumberingService();
        var job = new EmployeeRenumberSideEffectJob(
            context, renumberingService, new FakeClock(new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc)),
            NullLogger<EmployeeRenumberSideEffectJob>.Instance);

        // Drive AttemptCount up to MaxAttempts (4) by repeatedly invoking ProcessAsync against a
        // service that always throws â€” each call increments AttemptCount by one via MarkProcessing.
        for (var attempt = 1; attempt < EmployeeRenumberSideEffectJob.MaxAttempts; attempt++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(message.Id, companyId));
        }

        var beforeFinal = await context.OutboxMessages.SingleAsync(m => m.Id == message.Id);
        Assert.Equal(EmployeeRenumberSideEffectJob.MaxAttempts - 1, beforeFinal.AttemptCount);
        Assert.Equal(OutboxMessage.StatusProcessing, beforeFinal.Status);

        // Final attempt: AttemptCount reaches MaxAttempts, so this is the final attempt.
        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(message.Id, companyId));

        var reloaded = await context.OutboxMessages.SingleAsync(m => m.Id == message.Id);
        Assert.Equal(EmployeeRenumberSideEffectJob.MaxAttempts, reloaded.AttemptCount);
        Assert.Equal(OutboxMessage.StatusFailed, reloaded.Status);
        Assert.NotNull(reloaded.FailedAt);
        Assert.Equal("Employee renumbering failed.", reloaded.ErrorMessage);
        Assert.Equal(EmployeeRenumberSideEffectJob.MaxAttempts, renumberingService.CallCount);
    }

    // OBT-REM-11: the caller-supplied companyId (used to scope the Hangfire failure audit to a
    // tenant) must be verified against the entity actually loaded, so a caller cannot enqueue a
    // job whose job-argument company id disagrees with the outbox row it operates on.
    [Fact]
    public async Task ProcessAsync_Throws_When_Supplied_CompanyId_Does_Not_Match_Outbox_Message()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc));
        var message = SeedPending(context, companyId, createdAt);

        var renumberingService = new CapturingEmployeeRenumberingService();
        var job = new EmployeeRenumberSideEffectJob(
            context, renumberingService, new FakeClock(new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc)),
            NullLogger<EmployeeRenumberSideEffectJob>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(message.Id, otherCompanyId));
        Assert.Equal(0, renumberingService.CallCount);
    }
}
