using HR.Modules.Employees.Contracts;

namespace HR.Modules.Assets.Tests.Infrastructure;

internal sealed class FakeDirectReportsReader : IDirectReportsReader
{
    public Task<IReadOnlyList<Guid>> GetDirectReportIdsAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Guid>>(new List<Guid>());

    public Task<IReadOnlyList<Guid>> GetAllDescendantIdsAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Guid>>(new List<Guid>());
}
