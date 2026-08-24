using HR.Modules.Employees.Contracts;

namespace HR.Modules.Probation.Tests.Infrastructure;

internal sealed class FakeEmployeeProbationDatesReader(EmployeeProbationDates? dates = null) : IEmployeeProbationDatesReader
{
    public Task<EmployeeProbationDates?> GetProbationDatesAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(dates);
}
