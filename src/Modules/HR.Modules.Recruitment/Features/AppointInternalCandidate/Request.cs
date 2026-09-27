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

    // Optional: falls back to the offer's proposed start date (same rule as HireCandidate). The
    // appointment fails when neither is available. A future date is scheduled and applied on that
    // date by the Employees module's daily promotions job; a past date must be confirmed.
    public DateOnly? EffectiveDate { get; init; }
    public bool ConfirmBackdatedEffectiveDate { get; init; }

    // The employee's manager after the appointment. Exactly one of ManagerId / NoManager must be set,
    // so a client can never clear the manager by simply omitting the field.
    public Guid? ManagerId { get; init; }
    public bool NoManager { get; init; }

    // Optional compensation change using the existing compensation model (recorded with reason
    // "RoleChange", effective from EffectiveDate).
    public bool CreateCompensationChange { get; init; }
    public string? CompensationSalaryType { get; init; }
    public decimal? CompensationSalary { get; init; }
    public string? CompensationCurrency { get; init; }
    public decimal? CompensationHoursPerWeek { get; init; }
    public decimal? CompensationFte { get; init; }
    public string? CompensationNotes { get; init; }
}
