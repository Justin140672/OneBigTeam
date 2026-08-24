using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Features.RecordSickness;

internal sealed record RecordSicknessRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid CategoryId { get; init; }
    public DateOnly StartDate { get; init; }
    public SicknessDayPart StartDayPart { get; init; }
    public DateOnly? EndDate { get; init; }
    public SicknessDayPart? EndDayPart { get; init; }
    public string? Notes { get; init; }

    // SICK-06: populated by the endpoint from the authenticated user's resolved identity — never
    // bound from the client body (internal properties are not touched by FastEndpoints' JSON
    // model binding). This is the manager/HR user recording the sickness, distinct from EmployeeId
    // (the affected employee).
    internal Guid? ActorEmployeeId { get; init; }
}
