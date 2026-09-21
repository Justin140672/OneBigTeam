using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Tests.Infrastructure;

internal sealed class FakeCompanyAdministratorDirectory : ICompanyAdministratorDirectory
{
    /// <summary>Per-company admin employee ids. Companies not present here return an empty list.</summary>
    public Dictionary<Guid, IReadOnlyList<Guid>> AdminEmployeeIdsByCompany { get; } = [];

    public Task<IReadOnlyList<Guid>> GetActiveCompanyAdministratorEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(AdminEmployeeIdsByCompany.GetValueOrDefault(companyId, (IReadOnlyList<Guid>)[]));
}
