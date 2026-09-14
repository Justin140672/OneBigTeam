namespace HR.Modules.Employees.Features.UpdateMyEmergencyContact;

internal sealed record UpdateMyEmergencyContactRequest
{
    public Guid CompanyId { get; init; }
    public Guid ContactId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Relationship { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string? Email { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
