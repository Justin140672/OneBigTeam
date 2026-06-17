namespace HR.Modules.Employees.Features.AddMyEmergencyContact;

internal sealed record AddMyEmergencyContactRequest
{
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Relationship { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string? Email { get; init; }
}
