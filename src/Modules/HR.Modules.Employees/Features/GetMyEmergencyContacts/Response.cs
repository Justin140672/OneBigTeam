namespace HR.Modules.Employees.Features.GetMyEmergencyContacts;

internal sealed record EmergencyContactItem(
    Guid Id,
    string Name,
    string Relationship,
    string PhoneNumber,
    string? Email);

internal sealed record GetMyEmergencyContactsResponse(List<EmergencyContactItem> Contacts);
