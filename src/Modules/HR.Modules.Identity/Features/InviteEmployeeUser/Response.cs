namespace HR.Modules.Identity.Features.InviteEmployeeUser;

internal sealed record InviteEmployeeUserResponse(Guid InviteId, Guid EmployeeId, string Email, DateTimeOffset ExpiresAt, bool EmailSent);
