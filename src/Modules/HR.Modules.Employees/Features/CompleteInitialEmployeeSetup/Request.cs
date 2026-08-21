namespace HR.Modules.Employees.Features.CompleteInitialEmployeeSetup;

internal sealed record CompleteInitialEmployeeSetupRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PreferredName { get; init; }
    public DateOnly DateOfBirth { get; init; }
    public string Nationality { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string? GenderOther { get; init; }
    public string? PersonalEmail { get; init; }
    public string? PhoneNumber { get; init; }
    public string? HomePhone { get; init; }
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string? County { get; init; }
    public string PostCode { get; init; } = string.Empty;
    public string? Country { get; init; }
}
