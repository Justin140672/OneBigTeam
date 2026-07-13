using HR.Infrastructure.Abstractions;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeEmployeeAudienceReader : IEmployeeAudienceReader
{
    public Dictionary<Guid, EmployeeAudienceProfile> EmployeeAudiences { get; } = new();
    public HashSet<Guid> ExistingDepartmentIds { get; } = [];
    public HashSet<Guid> ExistingLocationIds { get; } = [];
    public HashSet<Guid> ExistingPositionProfileIds { get; } = [];
    public HashSet<Guid> ExistingEmployeeIds { get; } = [];
    public Dictionary<Guid, string> DepartmentNames { get; } = new();
    public Dictionary<Guid, string> LocationNames { get; } = new();
    public Dictionary<Guid, string> PositionProfileNames { get; } = new();
    public List<Guid> EligibleEmployeeIds { get; set; } = [];

    public Task<EmployeeAudienceProfile?> GetEmployeeAudienceAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult(EmployeeAudiences.TryGetValue(employeeId, out var profile) ? profile : null);

    public Task<bool> DepartmentExistsAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken) =>
        Task.FromResult(ExistingDepartmentIds.Contains(departmentId));

    public Task<bool> LocationExistsAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken) =>
        Task.FromResult(ExistingLocationIds.Contains(locationId));

    public Task<bool> PositionProfileExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
        Task.FromResult(ExistingPositionProfileIds.Contains(positionProfileId));

    public Task<bool> EmployeeExistsAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult(ExistingEmployeeIds.Contains(employeeId));

    public Task<string?> GetDepartmentNameAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken) =>
        Task.FromResult(DepartmentNames.TryGetValue(departmentId, out var name) ? name : null);

    public Task<string?> GetLocationNameAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken) =>
        Task.FromResult(LocationNames.TryGetValue(locationId, out var name) ? name : null);

    public Task<string?> GetPositionProfileNameAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
        Task.FromResult(PositionProfileNames.TryGetValue(positionProfileId, out var name) ? name : null);

    public Task<IReadOnlyList<Guid>> GetEligibleEmployeeIdsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> departmentIds,
        IReadOnlyCollection<Guid> locationIds,
        IReadOnlyCollection<Guid> positionProfileIds,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(EligibleEmployeeIds);
}
