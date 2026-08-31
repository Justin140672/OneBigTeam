namespace HR.Modules.Employees.Features.GetManagerTeamStatusSummary;

/// <summary>
/// DSH-05: both values are route segments. <see cref="ManagerId"/> is only ever a target selector
/// — it is authorized against the authenticated caller in the endpoint (caller must BE that
/// manager, sit ABOVE them in the reporting tree, or hold company-wide employee access). It is
/// never trusted as the authorization identity. See
/// specifications/architecture/11-manager-hierarchy-scope.md.
/// </summary>
internal sealed record GetManagerTeamStatusSummaryRequest(Guid CompanyId, Guid ManagerId);
