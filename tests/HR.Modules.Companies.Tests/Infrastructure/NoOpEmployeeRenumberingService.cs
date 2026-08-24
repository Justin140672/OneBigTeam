using HR.Modules.Employees.Contracts;

namespace HR.Modules.Companies.Tests.Infrastructure;

internal sealed class NoOpEmployeeRenumberingService : IEmployeeRenumberingService
{
    public Task RenumberAllEmployeesAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

internal sealed class CapturingEmployeeRenumberingService : IEmployeeRenumberingService
{
    private readonly List<Guid> _calls = [];
    public IReadOnlyList<Guid> Calls => _calls;
    public int CallCount => _calls.Count;

    public Task RenumberAllEmployeesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        _calls.Add(companyId);
        return Task.CompletedTask;
    }
}
