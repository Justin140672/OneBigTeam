using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateTimelineEntryOnCompensationChanged;
using HR.Modules.Employees.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class CompensationChangedHandlerTests
{
    [Fact]
    public async Task HandleAsync_Writes_HrOnly_Timeline_Entry_With_No_Salary_Figure()
    {
        var timelineWriter = new FakeEmployeeTimelineWriter();
        var handler = new CompensationChangedHandler(timelineWriter);

        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var compensationId = Guid.NewGuid();
        var effectiveFrom = new DateOnly(2026, 8, 1);
        var occurredAt = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new CompensationChangedIntegrationEvent(companyId, employeeId, compensationId, effectiveFrom, "Annual", "AnnualReview", occurredAt),
            CancellationToken.None);

        var entry = Assert.Single(timelineWriter.Added);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(effectiveFrom, entry.EventDate);
        Assert.Equal(EmployeeTimelineEventType.CompensationChanged, entry.EventType);
        Assert.Equal(EmployeeTimelineCategory.Compensation, entry.Category);
        Assert.Equal(EmployeeTimelineVisibility.HrOnly, entry.Visibility);
        Assert.Equal(compensationId, entry.SourceRecordId);

        // Redaction rule: no salary/amount figure may ever be persisted into Title/Summary,
        // regardless of visibility tier.
        Assert.DoesNotContain("Annual", entry.Summary);
        Assert.DoesNotContain("AnnualReview", entry.Summary);
        Assert.Matches("^[^0-9]*$", entry.Summary);
    }
}
