using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateTimelineEntryOnOnboardingCompleted;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class OnboardingCompletedHandlerTests
{
    [Fact]
    public async Task HandleAsync_Writes_Timeline_Entry()
    {
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new OnboardingCompletedHandler(timelineWriter);

        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var onboardingPlanId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new OnboardingCompletedIntegrationEvent(companyId, employeeId, onboardingPlanId, occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(DateOnly.FromDateTime(occurredAt.DateTime), entry.EventDate);
        Assert.Equal(EmployeeTimelineEventType.OnboardingCompleted, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.OnboardingAndProbation, entry.Category);
        Assert.Equal(EmployeeTimelineVisibility.AuthorisedInternal, entry.Visibility);
        Assert.Equal(onboardingPlanId, entry.SourceRecordId);
        Assert.Equal("Onboarding", entry.SourceModule);
    }
}
