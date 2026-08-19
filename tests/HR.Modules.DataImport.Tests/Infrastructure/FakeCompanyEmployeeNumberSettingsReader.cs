using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.DataImport.Tests.Infrastructure;

// Manual mode by default, matching the pre-existing behaviour where every employee number was
// manually supplied in staged rows — tests that don't care about automatic numbering aren't
// affected.
internal sealed class FakeCompanyEmployeeNumberSettingsReader(EmployeeNumberMode mode = EmployeeNumberMode.Manual)
    : ICompanyEmployeeNumberSettingsReader
{
    public Task<EmployeeNumberMode> GetModeAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(mode);

    public Task<EmployeeNumberSequencePreview> GetSequencePreviewAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(new EmployeeNumberSequencePreview(null, 1, 1));
}
