using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Services;

// Resolution order: Employee override (if both unit+length are set) -> Position Profile override
// (if both unit+length are set) -> company default (always available via
// ICompanyNoticePeriodSettingsReader). Mirrors ProbationDateResolver's style of accepting the raw
// override values the caller already has in hand rather than re-querying the DB itself.
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
        // Both-or-neither is enforced at the API boundary (validators), but a single mismatched
        // value here is treated defensively as "not overridden at this level" rather than thrown.
        if (employeeUnitOverride.HasValue && employeeLengthOverride.HasValue)
            return new EffectiveNoticePeriod(employeeUnitOverride.Value, employeeLengthOverride.Value, NoticePeriodSource.Employee);

        if (positionProfileUnitOverride.HasValue && positionProfileLengthOverride.HasValue)
            return new EffectiveNoticePeriod(positionProfileUnitOverride.Value, positionProfileLengthOverride.Value, NoticePeriodSource.PositionProfile);

        var (unit, length) = await companySettingsReader.GetDefaultNoticePeriodAsync(companyId, cancellationToken);
        return new EffectiveNoticePeriod(unit, length, NoticePeriodSource.CompanyDefault);
    }
}
