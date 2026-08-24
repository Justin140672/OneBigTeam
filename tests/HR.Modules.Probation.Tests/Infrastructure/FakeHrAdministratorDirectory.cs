using HR.Infrastructure.Abstractions;

namespace HR.Modules.Probation.Tests.Infrastructure;

internal sealed class FakeHrAdministratorDirectory : IHrAdministratorDirectory
{
    private readonly Dictionary<Guid, List<Guid>> _byCompany = new();

    public void Seed(Guid companyId, params Guid[] employeeIds) => _byCompany[companyId] = employeeIds.ToList();

    public Task<IReadOnlyList<Guid>> GetHrAdministratorEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(_byCompany.TryGetValue(companyId, out var ids) ? ids : []);
}
