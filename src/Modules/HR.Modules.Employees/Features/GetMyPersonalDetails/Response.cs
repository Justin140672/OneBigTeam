namespace HR.Modules.Employees.Features.GetMyPersonalDetails;

internal sealed record GetMyPersonalDetailsResponse(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string? PreferredName,
    DateOnly? DateOfBirth,
    string? Nationality,
    string? Gender);
