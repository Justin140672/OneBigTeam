using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeEmployeeNameReader(Dictionary<Guid, string>? names = null) : IEmployeeNameReader
{
    private readonly IReadOnlyDictionary<Guid, string> _names =
        names ?? new Dictionary<Guid, string>();

    public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken) =>
        Task.FromResult(_names);
}
