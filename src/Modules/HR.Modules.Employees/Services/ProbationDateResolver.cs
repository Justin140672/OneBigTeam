using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees.Services;

internal sealed class ProbationDateResolver(ICompanyProbationSettingsReader companySettingsReader) : IProbationDateResolver
{
    public async Task<DateOnly> ResolveEndDateAsync(
        Guid companyId,
        int? positionMonthsOverride,
        DateOnly startDate,
        CancellationToken cancellationToken)
    {
        var months = positionMonthsOverride
            ?? await companySettingsReader.GetProbationMonthsAsync(companyId, cancellationToken);

        return startDate.AddMonths(months);
    }
}
