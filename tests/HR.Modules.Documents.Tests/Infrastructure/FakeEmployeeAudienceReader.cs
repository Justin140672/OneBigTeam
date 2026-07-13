using HR.Infrastructure.Abstractions;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeEmployeeAudienceReader : IEmployeeAudienceReader
{
    public Dictionary<Guid, (Guid? DepartmentId, Guid? LocationId)> EmployeeAudiences { get; } = new();
    public HashSet<Guid> ExistingDepartmentIds { get; } = [];
    public HashSet<Guid> ExistingLocationIds { get; } = [];
    public Dictionary<Guid, string> DepartmentNames { get; } = new();
    public Dictionary<Guid, string> LocationNames { get; } = new();
    public List<Guid> EligibleEmployeeIds { get; set; } = [];

    public Task<(Guid? DepartmentId, Guid? LocationId)> GetEmployeeAudienceAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult(EmployeeAudiences.TryGetValue(employeeId, out var audience) ? audience : ((Guid?)null, (Guid?)null));

    public Task<bool> DepartmentExistsAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken) =>
        Task.FromResult(ExistingDepartmentIds.Contains(departmentId));

    public Task<bool> LocationExistsAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken) =>
        Task.FromResult(ExistingLocationIds.Contains(locationId));

    public Task<string?> GetDepartmentNameAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken) =>
        Task.FromResult(DepartmentNames.TryGetValue(departmentId, out var name) ? name : null);

    public Task<string?> GetLocationNameAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken) =>
        Task.FromResult(LocationNames.TryGetValue(locationId, out var name) ? name : null);

    public Task<IReadOnlyList<Guid>> GetEligibleEmployeeIdsAsync(
        Guid companyId, Guid? departmentId, Guid? locationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(EligibleEmployeeIds);
}
