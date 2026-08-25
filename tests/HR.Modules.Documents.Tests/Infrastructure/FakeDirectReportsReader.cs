using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;

namespace HR.Modules.Documents.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="IDirectReportsReader"/> used to exercise DOC-01's
/// reporting-hierarchy resolution in <c>DocumentResourceAuthorizer</c> without a real database.
/// Construct with the fixed set of ids that should be returned as the caller's complete
/// (direct + indirect) reporting hierarchy. Mirrors
/// HR.Modules.Sickness.Tests.Infrastructure.FakeDirectReportsReader.
/// </summary>
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
