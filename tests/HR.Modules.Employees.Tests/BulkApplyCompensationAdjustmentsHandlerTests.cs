using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.BulkApplyCompensationAdjustments;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Modules.Employees.Tests;

public class BulkApplyCompensationAdjustmentsHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public async Task HandleAsync_Applies_Adjustments_To_Multiple_Employees_With_Shared_BulkOperationId()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();

        var employee1 = CreateEmployee(companyId, now);
        var employee2 = CreateEmployee(companyId, now);
        context.Employees.AddRange(employee1, employee2);

        var existing1 = Compensation.Create(Guid.NewGuid(), companyId, employee1.Id, new DateOnly(2025, 1, 1), SalaryType.Annual, 40000m, "GBP", 37.5m, 1m, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        var existing2 = Compensation.Create(Guid.NewGuid(), companyId, employee2.Id, new DateOnly(2025, 1, 1), SalaryType.Annual, 50000m, "GBP", 37.5m, 1m, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        context.Compensations.AddRange(existing1, existing2);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, publisher);

        var request = new BulkApplyCompensationAdjustmentsRequest
        {
            CompanyId = companyId,
            EffectiveDate = new DateOnly(2026, 1, 1),
            Reason = CompensationChangeReason.AnnualReview,
            AdjustmentMode = CompensationAdjustmentMode.PercentageIncrease,
            Items =
            [
                new BulkCompensationAdjustmentItem { EmployeeId = employee1.Id, ProposedSalary = 42000m, SalaryType = SalaryType.Annual, Currency = "GBP", HoursPerWeek = 37.5m, FTE = 1m },
                new BulkCompensationAdjustmentItem { EmployeeId = employee2.Id, ProposedSalary = 52500m, SalaryType = SalaryType.Annual, Currency = "GBP", HoursPerWeek = 37.5m, FTE = 1m }
            ]
        };

        var result = await handler.HandleAsync(request, ActorId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);

        var bulkOperationId = result.Value.BulkOperationId;
        Assert.NotEqual(Guid.Empty, bulkOperationId);

        Assert.Equal(2, await context.Compensations.CountAsync(c => c.EffectiveFrom == new DateOnly(2026, 1, 1)));

        // 2 closed + 2 bulk-applied events, all sharing the same CorrelationId (BulkOperationId).
        Assert.Equal(4, publisher.Published.Count);
        var bulkAppliedEvents = publisher.Published.OfType<CompensationRecordBulkAppliedAuditEvent>().ToList();
        Assert.Equal(2, bulkAppliedEvents.Count);
        Assert.All(bulkAppliedEvents, e => Assert.Equal(bulkOperationId, e.BulkOperationId));
        Assert.All(bulkAppliedEvents, e => Assert.Equal("PercentageIncrease", e.AdjustmentMode));

        var closedEvents = publisher.Published.OfType<CompensationRecordClosedAuditEvent>().ToList();
        Assert.Equal(2, closedEvents.Count);
    }

    [Fact]
    public async Task HandleAsync_Fails_And_Does_Not_Process_Remaining_Items_When_One_Item_Conflicts()
    {
        // NOTE: EF Core's InMemory provider does not support real transactions (BeginTransactionAsync
        // is a no-op; Rollback cannot undo already-committed SaveChangesAsync calls), so this test can
        // only verify the batch stops processing at the failing item and returns failure — it cannot
        // verify that already-written earlier items (e.g. employee1 below) get rolled back. That
        // stronger "nothing partially applied" guarantee is covered against a real Postgres instance in
        // BulkApplyCompensationAdjustmentsEndpointTests (HR.Integration.Tests), which uses Testcontainers.
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();

        var employee1 = CreateEmployee(companyId, now);
        var employee2 = CreateEmployee(companyId, now); // will conflict
        var employee3 = CreateEmployee(companyId, now);
        context.Employees.AddRange(employee1, employee2, employee3);

        // Employee2 already has an open record starting on the same date as the requested effective date,
        // which the writer treats as a conflict (not a "close and replace" scenario).
        var conflicting = Compensation.Create(Guid.NewGuid(), companyId, employee2.Id, new DateOnly(2026, 1, 1), SalaryType.Annual, 40000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        context.Compensations.Add(conflicting);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, publisher);

        var request = new BulkApplyCompensationAdjustmentsRequest
        {
            CompanyId = companyId,
            EffectiveDate = new DateOnly(2026, 1, 1),
            Reason = CompensationChangeReason.AnnualReview,
            AdjustmentMode = CompensationAdjustmentMode.FixedAmountIncrease,
            Items =
            [
                new BulkCompensationAdjustmentItem { EmployeeId = employee1.Id, ProposedSalary = 45000m, SalaryType = SalaryType.Annual, Currency = "GBP" },
                new BulkCompensationAdjustmentItem { EmployeeId = employee2.Id, ProposedSalary = 41000m, SalaryType = SalaryType.Annual, Currency = "GBP" },
                new BulkCompensationAdjustmentItem { EmployeeId = employee3.Id, ProposedSalary = 47000m, SalaryType = SalaryType.Annual, Currency = "GBP" }
            ]
        };

        var result = await handler.HandleAsync(request, ActorId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains(employee2.Id.ToString(), result.Error.Message);

        // Employee3 comes after the conflicting employee2 in the batch, so the loop must never reach it.
        Assert.Empty(await context.Compensations.Where(c => c.EmployeeId == employee3.Id).ToListAsync());
        Assert.Single(await context.Compensations.Where(c => c.EmployeeId == employee2.Id).ToListAsync());
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_An_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var publisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, publisher);

        var request = new BulkApplyCompensationAdjustmentsRequest
        {
            CompanyId = companyId,
            EffectiveDate = new DateOnly(2026, 1, 1),
            Reason = CompensationChangeReason.AnnualReview,
            AdjustmentMode = CompensationAdjustmentMode.SetDirectly,
            Items = [new BulkCompensationAdjustmentItem { EmployeeId = Guid.NewGuid(), ProposedSalary = 45000m, SalaryType = SalaryType.Annual, Currency = "GBP" }]
        };

        var result = await handler.HandleAsync(request, ActorId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Publishes_CompensationChanged_IntegrationEvent_Per_Item_With_No_Salary_Figure()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();

        var employee1 = CreateEmployee(companyId, now);
        var employee2 = CreateEmployee(companyId, now);
        context.Employees.AddRange(employee1, employee2);
        await context.SaveChangesAsync();

        var integrationPublisher = new CapturingIntegrationEventPublisher();
        var handler = BuildHandler(context, new FakeAuditPublisher(), integrationPublisher);

        var request = new BulkApplyCompensationAdjustmentsRequest
        {
            CompanyId = companyId,
            EffectiveDate = new DateOnly(2026, 1, 1),
            Reason = CompensationChangeReason.AnnualReview,
            AdjustmentMode = CompensationAdjustmentMode.PercentageIncrease,
            Items =
            [
                new BulkCompensationAdjustmentItem { EmployeeId = employee1.Id, ProposedSalary = 42000m, SalaryType = SalaryType.Annual, Currency = "GBP" },
                new BulkCompensationAdjustmentItem { EmployeeId = employee2.Id, ProposedSalary = 52500m, SalaryType = SalaryType.Annual, Currency = "GBP" }
            ]
        };

        var result = await handler.HandleAsync(request, ActorId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var compensationEvents = integrationPublisher.Published.OfType<CompensationChangedIntegrationEvent>().ToList();
        Assert.Equal(2, compensationEvents.Count);
        Assert.Contains(compensationEvents, e => e.EmployeeId == employee1.Id);
        Assert.Contains(compensationEvents, e => e.EmployeeId == employee2.Id);

        // Redaction: no salary/amount figure exists on this event type at all (no such field) —
        // assert the serialized form never contains either proposed salary as a defence-in-depth check.
        foreach (var evt in compensationEvents)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(evt);
            Assert.DoesNotContain("42000", json);
            Assert.DoesNotContain("52500", json);
        }
    }

    private static BulkApplyCompensationAdjustmentsHandler BuildHandler(
        EmployeesDbContext context, FakeAuditPublisher publisher, HR.SharedKernel.IIntegrationEventPublisher? integrationEventPublisher = null) =>
        new(context, new CompensationRecordWriter(context, new FakeClock(FixedUtcNow)), new FakeClock(FixedUtcNow), publisher, integrationEventPublisher ?? new NoOpIntegrationEventPublisher());

    private static Employee CreateEmployee(Guid companyId, DateTimeOffset now) =>
        Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com", new DateOnly(2024, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}"[..12], Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new EmployeesDbContext(options);
    }
}
