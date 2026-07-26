using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class EmployeeTimelineWriterTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static EmployeeTimelineEntry CreateEntry(
        Guid companyId,
        Guid employeeId,
        DateOnly? eventDate = null,
        EmployeeTimelineEventType eventType = EmployeeTimelineEventType.EmployeePromoted,
        string sourceModule = "Employees",
        Guid? sourceRecordId = null) =>
        EmployeeTimelineEntry.Create(
            Guid.NewGuid(),
            companyId,
            employeeId,
            eventDate ?? new DateOnly(2026, 7, 20),
            eventType,
            EmployeeTimelineCategory.Employment,
            "Employee promoted",
            "Employee was promoted.",
            performedByUserId: null,
            sourceModule,
            sourceRecordId,
            EmployeeTimelineVisibility.AuthorisedInternal,
            FixedNow);

    [Fact]
    public async Task TryAddAsync_Returns_True_On_First_Insert()
    {
        await using var context = BuildContext();
        var writer = new EmployeeTimelineWriter(context);
        var entry = CreateEntry(Guid.NewGuid(), Guid.NewGuid(), sourceRecordId: Guid.NewGuid());

        var result = await writer.TryAddAsync(entry, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, await context.EmployeeTimelineEntries.CountAsync());
    }

    [Fact]
    public async Task TryAddAsync_Returns_False_For_Exact_Duplicate_By_SourceRecordId_Key()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var sourceRecordId = Guid.NewGuid();

        var writer = new EmployeeTimelineWriter(context);
        var first = CreateEntry(companyId, employeeId, sourceRecordId: sourceRecordId);
        var firstResult = await writer.TryAddAsync(first, CancellationToken.None);
        Assert.True(firstResult);

        // Same company/source module/event type/source record id — differing only by id/title
        // should still be treated as a duplicate by the natural key check.
        var duplicate = CreateEntry(companyId, employeeId, sourceRecordId: sourceRecordId);
        var duplicateResult = await writer.TryAddAsync(duplicate, CancellationToken.None);

        Assert.False(duplicateResult);
        Assert.Equal(1, await context.EmployeeTimelineEntries.CountAsync());
    }

    [Fact]
    public async Task TryAddAsync_Returns_True_For_Different_SourceRecordId_Even_If_Other_Fields_Match()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var writer = new EmployeeTimelineWriter(context);
        var first = CreateEntry(companyId, employeeId, sourceRecordId: Guid.NewGuid());
        await writer.TryAddAsync(first, CancellationToken.None);

        var second = CreateEntry(companyId, employeeId, sourceRecordId: Guid.NewGuid());
        var result = await writer.TryAddAsync(second, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(2, await context.EmployeeTimelineEntries.CountAsync());
    }

    [Fact]
    public async Task TryAddAsync_Returns_False_For_Exact_Duplicate_With_Null_SourceRecordId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var eventDate = new DateOnly(2026, 7, 20);

        var writer = new EmployeeTimelineWriter(context);
        var first = CreateEntry(
            companyId, employeeId, eventDate, EmployeeTimelineEventType.ManagerChanged, sourceRecordId: null);
        var firstResult = await writer.TryAddAsync(first, CancellationToken.None);
        Assert.True(firstResult);

        // Same company/employee/event type/event date, both with a null SourceRecordId — this is
        // the natural key used for the null case.
        var duplicate = CreateEntry(
            companyId, employeeId, eventDate, EmployeeTimelineEventType.ManagerChanged, sourceRecordId: null);
        var duplicateResult = await writer.TryAddAsync(duplicate, CancellationToken.None);

        Assert.False(duplicateResult);
        Assert.Equal(1, await context.EmployeeTimelineEntries.CountAsync());
    }

    [Fact]
    public async Task TryAddAsync_Does_Not_Treat_Null_And_NonNull_SourceRecordId_As_The_Same_Entry()
    {
        // A null SourceRecordId entry and a non-null one for the same employee/event type/date
        // are deduplicated by different natural keys and should not collide with each other.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var eventDate = new DateOnly(2026, 7, 20);

        var writer = new EmployeeTimelineWriter(context);
        var withNullSourceRecordId = CreateEntry(
            companyId, employeeId, eventDate, EmployeeTimelineEventType.ManagerChanged, sourceRecordId: null);
        await writer.TryAddAsync(withNullSourceRecordId, CancellationToken.None);

        var withSourceRecordId = CreateEntry(
            companyId, employeeId, eventDate, EmployeeTimelineEventType.ManagerChanged, sourceRecordId: Guid.NewGuid());
        var result = await writer.TryAddAsync(withSourceRecordId, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(2, await context.EmployeeTimelineEntries.CountAsync());
    }

    [Fact]
    public async Task TryAddAsync_Allows_Same_Natural_Key_For_Different_Companies()
    {
        await using var context = BuildContext();
        var employeeId = Guid.NewGuid();
        var sourceRecordId = Guid.NewGuid();

        var writer = new EmployeeTimelineWriter(context);
        var forCompanyA = CreateEntry(Guid.NewGuid(), employeeId, sourceRecordId: sourceRecordId);
        var forCompanyB = CreateEntry(Guid.NewGuid(), employeeId, sourceRecordId: sourceRecordId);

        Assert.True(await writer.TryAddAsync(forCompanyA, CancellationToken.None));
        Assert.True(await writer.TryAddAsync(forCompanyB, CancellationToken.None));
        Assert.Equal(2, await context.EmployeeTimelineEntries.CountAsync());
    }
}
