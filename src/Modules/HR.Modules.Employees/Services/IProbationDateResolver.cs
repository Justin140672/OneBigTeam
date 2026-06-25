namespace HR.Modules.Employees.Services;

internal interface IProbationDateResolver
{
    Task<DateOnly> ResolveEndDateAsync(
        Guid companyId,
        int? positionMonthsOverride,
        DateOnly startDate,
        CancellationToken cancellationToken);
}
