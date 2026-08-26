using HR.Modules.Employees.Contracts;

namespace HR.Modules.Notifications.Tests.Infrastructure;

/// <summary>Only implements the single member NotifyOnEmployeeCreatedHandler actually calls; every
/// other member throws NotImplementedException by design, so an accidental new call site is caught
/// immediately by a test failure rather than silently returning a default value.</summary>
internal sealed class FakePositionProfileReader : IPositionProfileReader
{
    public PositionProfileSummary? Summary { get; set; }

    public Task<PositionProfileSummary?> GetSummaryAsync(
        Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
        Task.FromResult(Summary);

    public Task<bool> ExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<Guid>> FindActiveMatchesAsync(
        Guid companyId, Guid? departmentId, string title, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<Guid?> GetDepartmentIdAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<PositionProfileSummary>> GetSummariesAsync(
        Guid companyId, IReadOnlyCollection<Guid> positionProfileIds, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<Guid>> GetIdsByDepartmentAsync(
        Guid companyId, Guid departmentId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<PositionProfileEmploymentDefaults?> GetEmploymentDefaultsAsync(
        Guid companyId, Guid positionProfileId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<Guid>> GetAllActiveIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<Guid>> GetAllIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
