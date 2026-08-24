namespace HR.Modules.Leave.Features.DeactivateLeaveType;

internal sealed record DeactivateLeaveTypeRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }

    // Populated by the endpoint from the authenticated user's "sub" claim — never bound from the
    // client body (internal properties are not touched by FastEndpoints' JSON model binding).
    internal Guid? ActorEmployeeId { get; init; }
}
