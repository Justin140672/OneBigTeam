namespace HR.Modules.Employees.Features.GetMyContactDetails;

internal sealed record GetMyContactDetailsResponse(
    string WorkEmail,
    string? PersonalEmail,
    string? PhoneNumber,
    string? HomePhone,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? County,
    string? PostCode,
    string? Country);
