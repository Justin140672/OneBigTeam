using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

internal sealed class FakeEmployeeRecruiterReader : IEmployeeRecruiterReader
{
    public Dictionary<Guid, string> RecruiterNames { get; set; } = [];

    public Task<IReadOnlyDictionary<Guid, string>> GetRecruiterNamesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(RecruiterNames);
}
