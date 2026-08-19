using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Leave.Tests.Infrastructure;

internal sealed class FakeDirectReportsReader(params Guid[] reportIds) : IDirectReportsReader
{
    private readonly IReadOnlyList<Guid> _reportIds = reportIds;

    public Task<IReadOnlyList<Guid>> GetDirectReportIdsAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_reportIds);
}
