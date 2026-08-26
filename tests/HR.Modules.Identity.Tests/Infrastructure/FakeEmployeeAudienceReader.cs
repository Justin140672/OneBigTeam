using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Identity.Tests.Infrastructure;

/// <summary>
/// Minimal test double for <see cref="IEmployeeAudienceReader"/> — only <see cref="GetAllEmployeeIdsAsync"/>
/// is used by InviteAdditionalUsersTask, so every other member is left unimplemented.
/// </summary>
internal sealed class FakeEmployeeAudienceReader : IEmployeeAudienceReader
{
    private readonly IReadOnlyList<Guid> _employeeIds;
    private readonly bool _exists;

    public FakeEmployeeAudienceReader(IReadOnlyList<Guid> employeeIds, bool exists = true)
    {
        _employeeIds = employeeIds;
        _exists = exists;
    }

    /// <summary>Captures the (companyId, employeeId) pair passed to the most recent <see cref="EmployeeExistsAsync"/> call.</summary>
    public (Guid CompanyId, Guid EmployeeId)? LastEmployeeExistsCall { get; private set; }

    public Task<EmployeeAudienceProfile?> GetEmployeeAudienceAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<IReadOnlyDictionary<Guid, EmployeeAudienceProfile>> GetEmployeeAudienceProfilesAsync(Guid companyId, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<EmployeeAudienceDetail>> GetEmployeeAudienceDetailsAsync(Guid companyId, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<bool> DepartmentExistsAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<bool> LocationExistsAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<bool> PositionProfileExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<bool> EmployeeExistsAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        LastEmployeeExistsCall = (companyId, employeeId);
        return Task.FromResult(_exists);
    }

    public Task<string?> GetDepartmentNameAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<string?> GetLocationNameAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<string?> GetPositionProfileNameAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<Guid>> GetEligibleEmployeeIdsAsync(Guid companyId, IReadOnlyCollection<Guid> departmentIds, IReadOnlyCollection<Guid> locationIds, IReadOnlyCollection<Guid> positionProfileIds, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<Guid>> GetAllEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(_employeeIds);
}
