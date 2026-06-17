namespace HR.Modules.Employees.Features.UpdateMyEmergencyContact;

internal sealed record UpdateMyEmergencyContactRequest
{
    public Guid CompanyId { get; init; }
    public Guid ContactId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Relationship { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string? Email { get; init; }
}
