namespace HR.Modules.Identity.Features.GetUserDetails;

internal sealed record GetUserDetailsRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
}
