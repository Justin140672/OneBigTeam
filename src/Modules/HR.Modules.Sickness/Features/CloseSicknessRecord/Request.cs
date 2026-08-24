using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Features.CloseSicknessRecord;

internal sealed record CloseSicknessRecordRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid Id { get; init; }
    public DateOnly EndDate { get; init; }
    public SicknessDayPart EndDayPart { get; init; }
    public DateOnly? ReturnToWorkDate { get; init; }
    public string? Notes { get; init; }

    // SICK-06: populated by the endpoint from the authenticated user's resolved identity — never
    // bound from the client body.
    internal Guid? ActorEmployeeId { get; init; }
}
