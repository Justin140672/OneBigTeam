namespace HR.Modules.Identity.Features.InviteEmployeeUser;

internal sealed record InviteEmployeeUserRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public string Email { get; init; } = string.Empty;
    public List<Guid> RoleIds { get; init; } = [];

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
