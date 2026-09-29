using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

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

public interface IEmployeeInternalAppointmentService
{
    Task<Result<InternalAppointmentResult>> AppointAsync(
        InternalAppointmentRequest request,
        CancellationToken cancellationToken);

    Task<InternalAppointmentResult?> ResumeBySourceReferenceAsync(
        Guid companyId,
        string sourceReference,
        Guid performedByUserId,
        CancellationToken cancellationToken);
}
