using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Tests.Infrastructure;

internal sealed class FakeCompanyAdministratorDirectory : ICompanyAdministratorDirectory
{
    public Dictionary<Guid, IReadOnlyList<Guid>> AdminEmployeeIdsByCompany { get; } = [];

    public Task<IReadOnlyList<Guid>> GetActiveCompanyAdministratorEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(AdminEmployeeIdsByCompany.GetValueOrDefault(companyId, (IReadOnlyList<Guid>)[]));
}
