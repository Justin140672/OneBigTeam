using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

/// <summary>
/// Optional compensation change recorded with an internal appointment, using the existing
/// compensation model. <see cref="SalaryType"/> is the name of the Employees module's own salary type
/// ("Annual", "Hourly" or "Daily") because that enum is internal to the module.
/// </summary>
public sealed record InternalAppointmentCompensation(
    string SalaryType,
    decimal Salary,
    string Currency,
    decimal? HoursPerWeek,
    decimal? Fte,
    string? Notes);

/// <summary>
/// Internal recruitment Ticket 7: moves an EXISTING employee into the role advertised by a vacancy
/// they applied for internally. The new department and location are always taken from
/// <see cref="NewPositionProfileId"/> by the Employees module; they are never caller-supplied.
/// </summary>
/// <param name="NewManagerId">The manager the employee reports to after the appointment. Null means
/// the employee is deliberately left with no manager (not "keep the current manager").</param>
/// <param name="SourceReference">Stable idempotency key, e.g. "recruitment:application:{id}". A retry
/// with the same key returns the change already recorded instead of recording a second one.</param>
public sealed record InternalAppointmentRequest(
    Guid CompanyId,
    Guid EmployeeId,
    Guid NewPositionProfileId,
    DateOnly EffectiveDate,
    Guid? NewManagerId,
    string SourceReference,
    string Reason,
    Guid PerformedByUserId,
    bool ConfirmBackdatedEffectiveDate,
    InternalAppointmentCompensation? Compensation);

/// <summary>The employee change recorded for an internal appointment.</summary>
/// <param name="IsApplied">True once the change has been applied to the employee record; false while a
/// future-dated change is scheduled (it is applied by the daily promotions job on the effective date).</param>
/// <param name="WasAlreadyRecorded">True when this call found the change recorded by an earlier attempt
/// with the same source reference and recorded nothing new.</param>
public sealed record InternalAppointmentResult(
    Guid PromotionId,
    Guid EmployeeId,
    Guid PreviousPositionProfileId,
    Guid NewPositionProfileId,
    Guid NewDepartmentId,
    Guid NewLocationId,
    Guid? NewManagerId,
    DateOnly EffectiveDate,
    Guid? CompensationId,
    bool IsApplied,
    bool WasAlreadyRecorded);

/// <summary>
/// Cross-module port implemented by HR.Modules.Employees and consumed by HR.Modules.Recruitment's
/// internal appointment workflow. It records the change through the Employees module's existing
/// promotion mechanism (promotion history, timeline, scheduled application of future-dated changes).
/// It never creates an employee and never publishes EmployeeCreated.
/// </summary>
public interface IEmployeeInternalAppointmentService
{
    /// <summary>
    /// Records (and, when the effective date is today or earlier, applies) the appointment. Returns a
    /// failure only when nothing was committed; a repeated call with the same
    /// <see cref="InternalAppointmentRequest.SourceReference"/> returns the existing change.
    /// </summary>
    Task<Result<InternalAppointmentResult>> AppointAsync(
        InternalAppointmentRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Recovery: returns the change already recorded for <paramref name="sourceReference"/> in the
    /// company (applying it first if it is due but an interrupted attempt never applied it), or null
    /// when nothing was recorded. Used to complete an appointment interrupted after the Employees side
    /// committed. Records nothing new.
    /// </summary>
    Task<InternalAppointmentResult?> ResumeBySourceReferenceAsync(
        Guid companyId,
        string sourceReference,
        Guid performedByUserId,
        CancellationToken cancellationToken);
}
