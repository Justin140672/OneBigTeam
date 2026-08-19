using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.DataImport.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IEmployeeImportWriter"/>. Records every call so
/// ConfirmImportSessionHandler tests can assert on what was requested, without needing a live
/// EmployeesDbContext. Supports seeding per-row failures (to exercise the handler's per-row
/// exception handling) and per-employee TryAssignManagerAsync outcomes.
/// </summary>
internal sealed class FakeEmployeeImportWriter : IEmployeeImportWriter
{
    private readonly HashSet<string> _workEmailsThatThrow = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _managerAssignmentsThatFail = [];
    private bool _failAllManagerAssignments;

    public List<EmployeeImportCreateRequest> CreateRequests { get; } = [];
    public List<(Guid CompanyId, Guid EmployeeId, EmployeeImportWorkingPattern Pattern)> WorkingPatternCalls { get; } = [];
    public List<(Guid CompanyId, Guid EmployeeId, EmployeeImportCompensation Compensation)> CompensationCalls { get; } = [];
    public List<(Guid CompanyId, Guid EmployeeId, Guid ManagerId)> ManagerAssignmentAttempts { get; } = [];

    public void FailCreationFor(string workEmail) => _workEmailsThatThrow.Add(workEmail.Trim());

    public void FailManagerAssignmentFor(Guid employeeId) => _managerAssignmentsThatFail.Add(employeeId);

    public void FailAllManagerAssignments() => _failAllManagerAssignments = true;

    public Task<EmployeeImportCreateResult> CreateEmployeeAsync(
        EmployeeImportCreateRequest request, CancellationToken cancellationToken)
    {
        CreateRequests.Add(request);

        if (_workEmailsThatThrow.Contains(request.WorkEmail.Trim()))
            throw new InvalidOperationException($"Simulated failure creating '{request.WorkEmail}'.");

        return Task.FromResult(new EmployeeImportCreateResult(
            request.Id,
            string.IsNullOrWhiteSpace(request.EmployeeNumber) ? "AUTO-00001" : request.EmployeeNumber,
            request.StartDate,
            ManagerId: null,
            request.PositionProfileId,
            ProbationEndDate: request.StartDate.AddMonths(6),
            DefaultLeavePolicyId: null));
    }

    public Task SetWorkingPatternAsync(
        Guid companyId, Guid employeeId, EmployeeImportWorkingPattern pattern, CancellationToken cancellationToken)
    {
        WorkingPatternCalls.Add((companyId, employeeId, pattern));
        return Task.CompletedTask;
    }

    public Task CreateOpeningCompensationAsync(
        Guid companyId, Guid employeeId, DateOnly effectiveFrom, EmployeeImportCompensation compensation,
        CancellationToken cancellationToken)
    {
        CompensationCalls.Add((companyId, employeeId, compensation));
        return Task.CompletedTask;
    }

    public Task<bool> TryAssignManagerAsync(
        Guid companyId, Guid employeeId, Guid managerId, CancellationToken cancellationToken)
    {
        ManagerAssignmentAttempts.Add((companyId, employeeId, managerId));
        return Task.FromResult(!_failAllManagerAssignments && !_managerAssignmentsThatFail.Contains(employeeId));
    }
}
