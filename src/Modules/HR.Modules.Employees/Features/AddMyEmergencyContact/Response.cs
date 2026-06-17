namespace HR.Modules.Employees.Features.AddMyEmergencyContact;

internal sealed record AddMyEmergencyContactResponse(
    Guid Id,
    string Name,
    string Relationship,
    string PhoneNumber,
    string? Email);
