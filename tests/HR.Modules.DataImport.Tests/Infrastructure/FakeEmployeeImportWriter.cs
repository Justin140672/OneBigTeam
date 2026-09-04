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
    private readonly Dictionary<Guid, EmployeeImportCreateResult> _snapshots = [];
    private bool _failAllManagerAssignments;

    public List<EmployeeImportCreateRequest> CreateRequests { get; } = [];
    public List<(Guid ExistingEmployeeId, EmployeeImportCreateRequest Request)> UpdateRequests { get; } = [];
    public List<(Guid CompanyId, Guid EmployeeId, EmployeeImportWorkingPattern Pattern)> WorkingPatternCalls { get; } = [];
    public List<(Guid CompanyId, Guid EmployeeId, EmployeeImportCompensation Compensation)> CompensationCalls { get; } = [];
    public List<(Guid CompanyId, Guid EmployeeId, Guid ManagerId)> ManagerAssignmentAttempts { get; } = [];

    /// <summary>
    /// OBT-REM-08: number of times the resume path (GetImportSnapshotAsync) was called, so tests
    /// can assert a retry read back an already-created employee instead of recreating it.
    /// </summary>
    public int GetImportSnapshotCalls { get; private set; }

    public void FailCreationFor(string workEmail) => _workEmailsThatThrow.Add(workEmail.Trim());

    /// <summary>
    /// Seeds a snapshot for an employee that was "already created by a previous attempt" without
    /// going through CreateEmployeeAsync in this test run - mirrors the resume path where
    /// ConfirmImportSessionHandler finds a staging row with CreatedEmployeeId already set.
    /// </summary>
    public void SeedSnapshot(Guid employeeId, EmployeeImportCreateResult result) => _snapshots[employeeId] = result;

    public void FailManagerAssignmentFor(Guid employeeId) => _managerAssignmentsThatFail.Add(employeeId);

    public void FailAllManagerAssignments() => _failAllManagerAssignments = true;

    public Task<EmployeeImportCreateResult> CreateEmployeeAsync(
        EmployeeImportCreateRequest request, CancellationToken cancellationToken)
    {
        CreateRequests.Add(request);

        if (_workEmailsThatThrow.Contains(request.WorkEmail.Trim()))
            throw new InvalidOperationException($"Simulated failure creating '{request.WorkEmail}'.");

        var result = new EmployeeImportCreateResult(
            request.Id,
            string.IsNullOrWhiteSpace(request.EmployeeNumber) ? "AUTO-00001" : request.EmployeeNumber,
            request.StartDate,
            ManagerId: null,
            request.PositionProfileId,
            ProbationEndDate: request.StartDate.AddMonths(6),
            DefaultLeavePolicyId: null);
        _snapshots[result.EmployeeId] = result;
        return Task.FromResult(result);
    }

    public Task<EmployeeImportCreateResult> UpdateEmployeeAsync(
        Guid existingEmployeeId, EmployeeImportCreateRequest request, CancellationToken cancellationToken)
    {
        UpdateRequests.Add((existingEmployeeId, request));

        if (_workEmailsThatThrow.Contains(request.WorkEmail.Trim()))
            throw new InvalidOperationException($"Simulated failure updating '{request.WorkEmail}'.");

        var result = new EmployeeImportCreateResult(
            existingEmployeeId,
            string.IsNullOrWhiteSpace(request.EmployeeNumber) ? "AUTO-00001" : request.EmployeeNumber,
            request.StartDate,
            ManagerId: null,
            request.PositionProfileId,
            ProbationEndDate: request.StartDate.AddMonths(6),
            DefaultLeavePolicyId: null);
        _snapshots[result.EmployeeId] = result;
        return Task.FromResult(result);
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

    public Task<EmployeeImportCreateResult?> GetImportSnapshotAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        GetImportSnapshotCalls++;
        return Task.FromResult(_snapshots.GetValueOrDefault(employeeId));
    }
}
