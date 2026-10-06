using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Services;

internal sealed record OfferTermsInput(
    Guid? ProposedManagerId,
    bool NoManager,
    string? Currency,
    decimal? HoursPerWeek,
    decimal? Fte,
    DateOnly? ResponseDeadline);

internal sealed class OfferTermsSnapshotFactory(
    IPositionProfileReader positionProfileReader,
    IEmploymentTypeReader employmentTypeReader,
    IEmployeeNameReader employeeNameReader)
{
    public async Task<OfferTermsSnapshot> BuildAsync(
        Vacancy vacancy,
        OfferTermsInput input,
        CancellationToken cancellationToken)
    {
        var summary = await positionProfileReader.GetSummaryAsync(
            vacancy.CompanyId, vacancy.PositionProfileId, cancellationToken);

        var defaults = await positionProfileReader.GetEmploymentDefaultsAsync(
            vacancy.CompanyId, vacancy.PositionProfileId, cancellationToken);

        string? employmentTypeName = null;
        if (vacancy.EmploymentTypeId is { } employmentTypeId)
            employmentTypeName = await employmentTypeReader.GetNameAsync(vacancy.CompanyId, employmentTypeId, cancellationToken);

        string? managerName = null;
        if (!input.NoManager && input.ProposedManagerId is { } managerId)
        {
            var names = await employeeNameReader.GetNamesAsync(vacancy.CompanyId, [managerId], cancellationToken);
            managerName = names.GetValueOrDefault(managerId);
        }

        var workingDays = defaults?.WorkingDaysOverride;
        var hoursPerDay = defaults?.HoursPerDayOverride;
        var hoursPerWeek = input.HoursPerWeek ?? DeriveHoursPerWeek(workingDays, hoursPerDay);

        return new OfferTermsSnapshot(
            vacancy.PositionProfileId,
            vacancy.AdvertTitle ?? summary?.Title ?? defaults?.Title,
            summary?.DepartmentId,
            summary?.DepartmentName,
            summary?.LocationId ?? defaults?.LocationId,
            summary?.LocationName ?? defaults?.LocationName,
            vacancy.EmploymentTypeId,
            employmentTypeName,
            input.NoManager ? null : input.ProposedManagerId,
            managerName,
            input.NoManager,
            string.IsNullOrWhiteSpace(input.Currency) ? null : input.Currency.Trim().ToUpperInvariant(),
            workingDays,
            hoursPerDay,
            hoursPerWeek,
            input.Fte,
            defaults?.ProbationMonthsOverride,
            input.ResponseDeadline);
    }

    private static decimal? DeriveHoursPerWeek(WorkingDays? workingDays, decimal? hoursPerDay)
    {
        if (workingDays is null or WorkingDays.None || hoursPerDay is null)
            return null;

        var dayCount = Enum.GetValues<WorkingDays>().Count(d => d != WorkingDays.None && workingDays.Value.HasFlag(d));
        return dayCount == 0 ? null : hoursPerDay.Value * dayCount;
    }
}
