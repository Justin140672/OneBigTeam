using HR.Modules.Employees.Contracts;
using HR.Modules.Offboarding.Features.RescheduleOffboardingOnLeavingDateChanged;
using HR.Modules.Offboarding.Tests.Infrastructure;

namespace HR.Modules.Offboarding.Tests.Features.RescheduleOffboardingOnLeavingDateChanged;

public class RescheduleOffboardingOnLeavingDateChangedHandlerTests
{
    [Fact]
    public async Task HandleAsync_Delegates_To_OffboardingPlanCoordinator_With_Event_CompanyId_EmployeeId_And_LastWorkingDay()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var coordinator = new FakeOffboardingPlanCoordinator();
        var handler = new RescheduleOffboardingOnLeavingDateChangedHandler(coordinator);

        var leavingDate = new DateOnly(2026, 8, 31);
        var lastWorkingDay = new DateOnly(2026, 8, 28);

        var integrationEvent = new EmployeeLeavingDateSetIntegrationEvent(
            companyId, employeeId, leavingDate, lastWorkingDay, DateTimeOffset.UtcNow);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var call = Assert.Single(coordinator.RescheduleOutstandingTasksCalls);
        Assert.Equal(companyId, call.CompanyId);
        Assert.Equal(employeeId, call.EmployeeId);
        Assert.Equal(lastWorkingDay, call.NewLastWorkingDay);
    }
}
