using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeDocumentTypeDefaultsProvisioner : IDocumentTypeDefaultsProvisioner
{
    public int CallCount { get; private set; }
    public List<Guid> RequestedCompanyIds { get; } = [];

    public Task EnsureDefaultDocumentTypesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        CallCount++;
        RequestedCompanyIds.Add(companyId);
        return Task.CompletedTask;
    }
}
