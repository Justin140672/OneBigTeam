using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateTimelineEntryOnProbationExtended;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class ProbationExtendedHandlerTests
{
    [Fact]
    public async Task HandleAsync_Writes_Timeline_Entry()
    {
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new ProbationExtendedHandler(timelineWriter);

        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var probationRecordId = Guid.NewGuid();
        var newExpectedEndDate = new DateOnly(2026, 12, 1);
        var occurredAt = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new ProbationExtendedIntegrationEvent(companyId, employeeId, probationRecordId, newExpectedEndDate, occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(DateOnly.FromDateTime(occurredAt.DateTime), entry.EventDate);
        Assert.Equal(EmployeeTimelineEventType.ProbationExtended, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.OnboardingAndProbation, entry.Category);
        Assert.Equal(EmployeeTimelineVisibility.AuthorisedInternal, entry.Visibility);
        Assert.Equal(probationRecordId, entry.SourceRecordId);
        Assert.Equal("Probation", entry.SourceModule);
    }

    [Fact]
    public async Task HandleAsync_Summary_Text_Contains_Only_New_Expected_End_Date_No_Free_Text_Reason()
    {
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new ProbationExtendedHandler(timelineWriter);

        var newExpectedEndDate = new DateOnly(2026, 12, 1);
        var occurredAt = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new ProbationExtendedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), newExpectedEndDate, occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal("Probation extended", entry.Title);
        Assert.Equal($"Probation period extended to {newExpectedEndDate:d MMM yyyy}.", entry.Summary);
    }
}
