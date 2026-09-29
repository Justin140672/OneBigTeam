namespace HR.Modules.Recruitment.Features.AppointInternalCandidate;

/// <summary>
/// Internal recruitment Ticket 7: completes a successful INTERNAL application by changing the
/// existing employee's role. Position profile, department and location are never client-supplied —
/// they are derived from the vacancy's position profile, exactly as for an external hire.
/// </summary>
internal sealed record AppointInternalCandidateRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }

    public DateOnly? EffectiveDate { get; init; }
    public bool ConfirmBackdatedEffectiveDate { get; init; }

    public Guid? ManagerId { get; init; }
    public bool NoManager { get; init; }

    public bool CreateCompensationChange { get; init; }
    public string? CompensationSalaryType { get; init; }
    public decimal? CompensationSalary { get; init; }
    public string? CompensationCurrency { get; init; }
    public decimal? CompensationHoursPerWeek { get; init; }
    public decimal? CompensationFte { get; init; }
    public string? CompensationNotes { get; init; }
}
