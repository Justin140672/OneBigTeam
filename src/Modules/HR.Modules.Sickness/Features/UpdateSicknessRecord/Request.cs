using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Features.UpdateSicknessRecord;

internal sealed record UpdateSicknessRecordRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid Id { get; init; }
    public Guid CategoryId { get; init; }
    public DateOnly StartDate { get; init; }
    public SicknessDayPart StartDayPart { get; init; }
    public string? Notes { get; init; }

    // Ticket 2 (optimistic concurrency) — see UpdateEmployeeProfileRequest.ExpectedVersion.
    public int? ExpectedVersion { get; init; }

    internal Guid? ActorEmployeeId { get; init; }
}
