namespace HR.Modules.Identity.Features.InviteEmployeeUser;

internal sealed record InviteEmployeeUserRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public string Email { get; init; } = string.Empty;
    public List<Guid> RoleIds { get; init; } = [];
}
