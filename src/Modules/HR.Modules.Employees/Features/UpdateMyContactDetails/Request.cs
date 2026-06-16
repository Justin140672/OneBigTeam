namespace HR.Modules.Employees.Features.UpdateMyContactDetails;

internal sealed record UpdateMyContactDetailsRequest
{
    public Guid CompanyId { get; init; }
    public string? PersonalEmail { get; init; }
    public string? PhoneNumber { get; init; }
    public string? HomePhone { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? County { get; init; }
    public string? PostCode { get; init; }
    public string? Country { get; init; }
}
