namespace HR.Modules.Identity.Features.ListInvitableEmployees;

internal sealed record ListInvitableEmployeesRequest
{
    public Guid CompanyId { get; init; }
}
