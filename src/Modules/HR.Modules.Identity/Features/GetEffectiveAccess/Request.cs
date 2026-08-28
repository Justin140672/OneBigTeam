namespace HR.Modules.Identity.Features.GetEffectiveAccess;

internal sealed record GetEffectiveAccessRequest(Guid CompanyId, Guid EmployeeId);
