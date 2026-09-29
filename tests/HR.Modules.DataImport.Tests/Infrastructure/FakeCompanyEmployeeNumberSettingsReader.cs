using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.DataImport.Tests.Infrastructure;

internal sealed class FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode mode = EmployeeNumberMode.Manual)
    : ICompanyEmployeeNumberSettingsReader
{
    public Task<EmployeeNumberMode> GetModeAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(mode);

    public Task<EmployeeNumberSequencePreview> GetSequencePreviewAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(new EmployeeNumberSequencePreview(null, 1, 1));
}
