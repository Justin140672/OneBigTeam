namespace HR.Modules.Identity.Authorization;

/// <summary>
/// Authoritative check that a target user/employee id actually belongs to the route company.
/// Tenant middleware (<see cref="HR.Modules.Identity.TenantRouteAuthorizationMiddleware"/>) only
/// proves the CALLER's company; it says nothing about which company owns the TARGET user named in
/// the route (e.g. GET /companies/{companyId}/users/{userId}). Without this check, any user ID
/// belonging to a different company that a caller can enumerate/guess is still resolved globally
/// by every user-administration handler (see IAM-01), letting an HR Administrator in Company A
/// read or modify a Company B user's roles/account status/personal details.
///
/// Deliberately built on the Employees module's own company-scoped read contract
/// (<see cref="HR.Modules.Employees.Contracts.IEmployeeAudienceReader.EmployeeExistsAsync"/>) rather
/// than Identity's own tables: ApplicationUser has no CompanyId column at all, and UserProfile rows
/// only exist for users that have completed AcceptInvite/SignUp (dev-seeded/legacy ApplicationUser
/// rows have neither). The Employees module is the single source of truth for which company an
/// employee id belongs to — the same association every user id is minted against (ApplicationUser.Id
/// == EmployeeId by convention across this module).
/// </summary>
internal interface ITargetUserCompanyGuard
{
    /// <summary>True only if <paramref name="userId"/> is an employee of <paramref name="companyId"/>.</summary>
    Task<bool> IsMemberAsync(Guid companyId, Guid userId, CancellationToken cancellationToken);
}
