using HR.Infrastructure.Abstractions;

namespace HR.Modules.DataImport.Tests.Infrastructure;

internal sealed class FakeLeaveImportWriter : ILeaveImportWriter
{
    private readonly bool _result;

    public FakeLeaveImportWriter(bool result = true) => _result = result;

    public List<(Guid CompanyId, Guid EmployeeId, string LeaveTypeCode, decimal OpeningBalanceDays)> Calls { get; } = [];

    public Task<bool> TryLayOpeningBalanceAsync(
        Guid companyId, Guid employeeId, string leaveTypeCode, decimal openingBalanceDays,
        Guid adjustedByEmployeeId, CancellationToken cancellationToken)
    {
        Calls.Add((companyId, employeeId, leaveTypeCode, openingBalanceDays));
        return Task.FromResult(_result);
    }
}
