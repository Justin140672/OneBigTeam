using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateTimelineEntryOnProbationPassed;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class ProbationPassedHandlerTests
{
    [Fact]
    public async Task HandleAsync_Writes_Timeline_Entry()
    {
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new ProbationPassedHandler(timelineWriter);

        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var probationRecordId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new ProbationPassedIntegrationEvent(companyId, employeeId, probationRecordId, occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(DateOnly.FromDateTime(occurredAt.DateTime), entry.EventDate);
        Assert.Equal(EmployeeTimelineEventType.ProbationPassed, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.OnboardingAndProbation, entry.Category);
        Assert.Equal(EmployeeTimelineVisibility.AuthorisedInternal, entry.Visibility);
        Assert.Equal(probationRecordId, entry.SourceRecordId);
        Assert.Equal("Probation", entry.SourceModule);
    }
}
