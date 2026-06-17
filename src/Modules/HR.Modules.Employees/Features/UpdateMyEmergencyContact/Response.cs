namespace HR.Modules.Employees.Features.UpdateMyEmergencyContact;

internal sealed record UpdateMyEmergencyContactResponse(
    Guid Id,
    string Name,
    string Relationship,
    string PhoneNumber,
    string? Email);
