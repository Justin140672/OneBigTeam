using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Employees.Services;

internal sealed class EffectiveNoticePeriodResolver(ICompanyNoticePeriodSettingsReader companySettingsReader)
    : IEffectiveNoticePeriodResolver
{
    public async Task<EffectiveNoticePeriod> ResolveAsync(
        Guid companyId,
        NoticePeriodUnit? employeeUnitOverride,
        int? employeeLengthOverride,
        NoticePeriodUnit? positionProfileUnitOverride,
        int? positionProfileLengthOverride,
        CancellationToken cancellationToken)
    {
        if (employeeUnitOverride.HasValue && employeeLengthOverride.HasValue)
            return new EffectiveNoticePeriod(employeeUnitOverride.Value, employeeLengthOverride.Value, NoticePeriodSource.Employee);

        if (positionProfileUnitOverride.HasValue && positionProfileLengthOverride.HasValue)
            return new EffectiveNoticePeriod(positionProfileUnitOverride.Value, positionProfileLengthOverride.Value, NoticePeriodSource.PositionProfile);

        var (unit, length) = await companySettingsReader.GetDefaultNoticePeriodAsync(companyId, cancellationToken);
        return new EffectiveNoticePeriod(unit, length, NoticePeriodSource.CompanyDefault);
    }
}
