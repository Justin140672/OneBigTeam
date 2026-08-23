using HR.Modules.Employees.Contracts;

namespace HR.Modules.Leave.Tests.Infrastructure;

internal sealed class FakeEmployeeStartDateReader(DateOnly? startDate) : IEmployeeStartDateReader
{
    public Task<DateOnly?> GetStartDateAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult(startDate);
}
