using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Services;

internal sealed record OfferTermsView(
    int OfferVersion,
    string? OfferResponseStatus,
    Guid? PositionProfileId,
    string? JobTitle,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LocationId,
    string? LocationName,
    Guid? EmploymentTypeId,
    string? EmploymentTypeName,
    Guid? ProposedManagerId,
    string? ProposedManagerName,
    bool NoManager,
    decimal? Salary,
    string? SalaryFrequency,
    string? Currency,
    DateOnly? ProposedStartDate,
    string[] WorkingDays,
    decimal? HoursPerDay,
    decimal? HoursPerWeek,
    decimal? Fte,
    int? ProbationMonths,
    DateOnly? OfferDate,
    DateOnly? ResponseDeadline,
    string? OfferNotes,
    DateTimeOffset? OfferMadeAt,
    DateTimeOffset? OfferRespondedAt,
    Guid? RespondedByUserId,
    string? ResponseChannel,
    string? ResponseReason,
    bool IsSnapshotComplete)
{
    public static OfferTermsView From(Application a) => new(
        a.OfferVersion,
        a.OfferResponseStatus?.ToString(),
        a.OfferPositionProfileId,
        a.OfferJobTitle,
        a.OfferDepartmentId,
        a.OfferDepartmentName,
        a.OfferLocationId,
        a.OfferLocationName,
        a.OfferEmploymentTypeId,
        a.OfferEmploymentTypeName,
        a.OfferProposedManagerId,
        a.OfferProposedManagerName,
        a.OfferNoManager,
        a.OfferedSalary,
        a.OfferedSalaryFrequency?.ToString(),
        a.OfferCurrency,
        a.OfferedStartDate,
        ExpandWorkingDays(a.OfferWorkingDays),
        a.OfferHoursPerDay,
        a.OfferHoursPerWeek,
        a.OfferFte,
        a.OfferProbationMonths,
        a.OfferDate,
        a.OfferResponseDeadline,
        a.OfferNotes,
        a.OfferMadeAt,
        a.OfferRespondedAt,
        a.OfferRespondedByUserId,
        a.OfferResponseChannel?.ToString(),
        a.OfferResponseReason,
        a.OfferTermsSnapshotAt is not null && a.HasManagerDecision && a.OfferCurrency is not null);

    private static string[] ExpandWorkingDays(HR.Modules.Employees.Contracts.WorkingDays? days)
    {
        if (days is null or HR.Modules.Employees.Contracts.WorkingDays.None)
            return [];

        return Enum.GetValues<HR.Modules.Employees.Contracts.WorkingDays>()
            .Where(d => d != HR.Modules.Employees.Contracts.WorkingDays.None && days.Value.HasFlag(d))
            .Select(d => d.ToString())
            .ToArray();
    }
}
