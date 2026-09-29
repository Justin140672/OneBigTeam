using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

internal sealed class FakeDirectReportsReader : IDirectReportsReader
{
    private readonly IReadOnlyList<Guid> _directReportIds;

    public FakeDirectReportsReader(IReadOnlyList<Guid>? directReportIds = null)
    {
        _directReportIds = directReportIds ?? [];
    }

    public Guid? LastCompanyId { get; private set; }
    public Guid? LastManagerId { get; private set; }

    public Task<IReadOnlyList<Guid>> GetDirectReportIdsAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastManagerId = managerId;

        return Task.FromResult(_directReportIds);
    }

    public Task<IReadOnlyList<Guid>> GetAllDescendantIdsAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastManagerId = managerId;

        return Task.FromResult(_directReportIds);
    }
}
