namespace HR.Modules.Probation.Features.MarkProbationNotApplicable;

/// <summary>
/// PROB-06: explicit "probation does not apply" decision. ManagerEmployeeId/StartDate/
/// ExpectedEndDate are only required when no probation record exists yet for the employee (i.e.
/// creation was deferred for lack of a manager/period) — see the handler for how they are used to
/// create a placeholder NotApplicable record in that case. When an in-flight (NotStarted/Active)
/// record already exists, only CompanyId/EmployeeId/Reason are needed.
/// </summary>
internal sealed record MarkProbationNotApplicableRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid? ManagerEmployeeId { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? ExpectedEndDate { get; init; }
    public string? Reason { get; init; }

    // PROB-07: populated by the endpoint from the authenticated user's resolved identity — never
    // bound from the client body.
    internal Guid? ActorEmployeeId { get; init; }
}
