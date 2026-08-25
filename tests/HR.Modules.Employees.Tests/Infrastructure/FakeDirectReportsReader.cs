using HR.Modules.Employees.Contracts;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeDirectReportsReader(params Guid[] reportIds) : IDirectReportsReader
{
    private readonly IReadOnlyList<Guid> _reportIds = reportIds;

    public Task<IReadOnlyList<Guid>> GetDirectReportIdsAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_reportIds);

    public Task<IReadOnlyList<Guid>> GetAllDescendantIdsAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_reportIds);
}
