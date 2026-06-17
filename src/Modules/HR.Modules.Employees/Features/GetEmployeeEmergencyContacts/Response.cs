namespace HR.Modules.Employees.Features.GetEmployeeEmergencyContacts;

internal sealed record EmployeeEmergencyContactItem(
    Guid Id,
    string Name,
    string Relationship,
    string PhoneNumber,
    string? Email);

internal sealed record GetEmployeeEmergencyContactsResponse(List<EmployeeEmergencyContactItem> Contacts);
