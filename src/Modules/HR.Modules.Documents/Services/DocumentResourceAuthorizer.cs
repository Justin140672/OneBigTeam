using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Resource-level (self / manager-hierarchy / HR administrator) authorization for Documents
/// endpoints that accept an employeeId route value. Endpoint-level Policies(...) only prove the
/// caller holds a role (e.g. "role:employee", which every Employee/Manager/Recruiter/HR
/// Administrator satisfies) — they never prove the caller has a relationship to the specific
/// employeeId in the route, so that check lives here and must be applied per endpoint before the
/// handler runs (DOC-01). Mirrors HR.Modules.Leave.Services.LeaveResourceAuthorizer /
/// HR.Modules.Sickness.Services.SicknessResourceAuthorizer /
/// HR.Modules.Probation.Services.ProbationResourceAuthorizer (LEAVE-02/SICK-02/PROB-02's
/// established "complete reporting hierarchy" scope decision).
///
/// Every employee-document endpoint (list, detail, download, delete, upload, document-request
/// read/write) must resolve its own <see cref="ICurrentUser.UserId"/>, verify company membership,
/// and call <see cref="CanAccessEmployeeDocumentsAsync"/> before touching the handler — routing
/// every such check through this single class (rather than ad-hoc inline role/permission checks
/// per endpoint) is what makes the rule hard for a future endpoint to accidentally omit.
/// </summary>
internal sealed class DocumentResourceAuthorizer(
    IAuthorizationService authorizationService,
    IDirectReportsReader directReportsReader)
{
    // Mirrors HR.Modules.Identity.Domain.SystemPermissions.DocumentManage. Documents cannot
    // reference Identity's internal SystemPermissions directly, so the permission id is
    // duplicated here as the sanctioned escape hatch — same pattern as
    // SicknessResourceAuthorizer.SicknessManagePermissionId. Only the HrAdministrator role is
    // currently granted "document.manage" (see RolePermissionConfiguration), making it a
    // reliable "is HR administrator" and "has company-wide document access" proxy. Every other
    // role (Employee, Manager, Recruiter) only holds "document.read", which the "role:employee"
    // endpoint policy already requires of everyone — so document.read cannot be used to
    // distinguish a manager's scope from a plain employee's, which is exactly why the
    // hierarchy/self checks below exist.
    private static readonly Guid DocumentManagePermissionId = new("00000000-0000-0000-0001-000000000010");

    public async Task<bool> IsHrAdministratorAsync(Guid callerEmployeeId, CancellationToken cancellationToken)
        => await authorizationService.HasPermissionAsync(callerEmployeeId, DocumentManagePermissionId, cancellationToken);

    /// <summary>
    /// Self, any manager in the target employee's complete reporting hierarchy (direct or
    /// indirect), or an HR Administrator may access the target employee's documents/document
    /// requests. Used to gate list, detail, download, delete, upload and document-request
    /// endpoints alike.
    /// </summary>
    public async Task<bool> CanAccessEmployeeDocumentsAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
    {
        if (callerEmployeeId == targetEmployeeId)
            return true;

        if (await IsHrAdministratorAsync(callerEmployeeId, cancellationToken))
            return true;

        var descendantIds = await directReportsReader.GetAllDescendantIdsAsync(
            companyId, callerEmployeeId, cancellationToken);

        return descendantIds.Contains(targetEmployeeId);
    }
}
