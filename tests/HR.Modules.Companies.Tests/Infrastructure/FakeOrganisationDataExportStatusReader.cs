using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Tests.Infrastructure;

internal sealed class FakeOrganisationDataExportStatusReader(bool hasActiveExport = false)
    : IOrganisationDataExportStatusReader
{
    public bool HasActiveExport { get; set; } = hasActiveExport;

    public Task<bool> HasActiveExportAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(HasActiveExport);
}
