using HR.Modules.Employees.Contracts;

namespace HR.Modules.Notifications.Tests.Infrastructure;

internal sealed class FakeEmployeeNameReader : IEmployeeNameReader
{
    public Dictionary<Guid, string> Names { get; } = [];

    public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, string> result = employeeIds
            .Where(Names.ContainsKey)
            .ToDictionary(id => id, id => Names[id]);
        return Task.FromResult(result);
    }
}
