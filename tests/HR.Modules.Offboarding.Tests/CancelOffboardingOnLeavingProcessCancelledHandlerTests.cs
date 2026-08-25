using HR.Modules.Employees.Contracts;
using HR.Modules.Offboarding.Features.CancelOffboardingOnLeavingProcessCancelled;
using HR.Modules.Offboarding.Tests.Infrastructure;

namespace HR.Modules.Offboarding.Tests;

public class CancelOffboardingOnLeavingProcessCancelledHandlerTests
{
    [Fact]
    public async Task HandleAsync_Delegates_To_OffboardingPlanCoordinator_With_Event_CompanyId_And_EmployeeId()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var coordinator = new FakeOffboardingPlanCoordinator();
        var handler = new CancelOffboardingOnLeavingProcessCancelledHandler(coordinator);

        var integrationEvent = new EmployeeLeavingProcessCancelledIntegrationEvent(
            companyId, employeeId, DateTimeOffset.UtcNow);

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var call = Assert.Single(coordinator.CancelOutstandingTasksCalls);
        Assert.Equal(companyId, call.CompanyId);
        Assert.Equal(employeeId, call.EmployeeId);
    }
}
