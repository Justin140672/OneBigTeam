using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Reporting.Jobs;

internal interface IOrganisationDataExportLeaseRenewer
{
    Task<bool> RenewAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken);
}

internal sealed class ScopedOrganisationDataExportLeaseRenewer(IServiceScopeFactory scopeFactory)
    : IOrganisationDataExportLeaseRenewer
{
    public async Task<bool> RenewAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOrganisationDataExportJobStore>();
        return await store.RenewLeaseAsync(exportId, ownerToken, cancellationToken);
    }
}
