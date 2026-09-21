using HR.Modules.Companies.Contracts;

namespace HR.Modules.Notifications.Tests.Infrastructure;

internal sealed class FakeActiveCompanyDirectory : IActiveCompanyDirectory
{
    public IReadOnlyList<Guid> ActiveCompanyIds { get; set; } = [];

    public Task<IReadOnlyList<Guid>> GetActiveCompanyIdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ActiveCompanyIds);
}
