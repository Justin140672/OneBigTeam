using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Authorization;

namespace HR.Modules.Sickness.Services;

/// <summary>
/// Resource-level (manager-hierarchy / HR administrator) authorization for Sickness endpoints
/// guarded by the "sickness:view-team" or "sickness:review" policies. Those policies only prove
/// the caller holds the Manager or HrAdministrator role — they never prove the caller has a
/// reporting relationship to the specific employee(s) whose sickness workflow data is being
/// requested, so that check lives here and is applied per endpoint before/around the handler
/// call. Standardised on the shared IAM-07 evaluation order by
/// HR.SharedKernel.Authorization.EmployeeResourceAuthorizer. Mirrors
/// HR.Modules.Leave.Services.LeaveResourceAuthorizer (SICK-02, following LEAVE-02's established
/// pattern and its "complete reporting hierarchy" scope decision).
/// </summary>
internal sealed class SicknessResourceAuthorizer
{
    // Mirrors HR.Modules.Identity.Domain.SystemPermissions.SicknessManage. Sickness cannot
    // reference Identity's internal SystemPermissions directly, so the permission id is
    // duplicated here as the sanctioned escape hatch — same pattern already used by
    // GetTeamSicknessToday/Endpoint.cs and RecordSickness/Endpoint.cs. Only the HrAdministrator
    // role is currently granted this permission, making it a reliable "is HR administrator" and
    // "has company-wide sickness access" proxy.
    private static readonly Guid SicknessManagePermissionId = new("00000000-0000-0000-0001-000000000015");

    private readonly IAuthorizationService _authorizationService;
    private readonly IDirectReportsReader _directReportsReader;
    private readonly EmployeeResourceAuthorizer _resourceAuthorizer;

    public SicknessResourceAuthorizer(
        IAuthorizationService authorizationService,
        IDirectReportsReader directReportsReader)
    {
        _authorizationService = authorizationService;
        _directReportsReader = directReportsReader;
        _resourceAuthorizer = new EmployeeResourceAuthorizer(
            IsHrAdministratorAsync,
            directReportsReader.GetAllDescendantIdsAsync);
    }

    public Task<bool> IsHrAdministratorAsync(Guid callerEmployeeId, CancellationToken cancellationToken)
        => _authorizationService.HasPermissionAsync(callerEmployeeId, SicknessManagePermissionId, cancellationToken);

    /// <summary>
    /// Resolves the set of employee ids the caller may view sickness workflow data for.
    /// Returns null when the caller is an HR Administrator, meaning access is unrestricted
    /// (company-wide) — callers should skip employee-id filtering entirely in that case rather
    /// than materialising the whole company as a set.
    /// </summary>
    public async Task<IReadOnlySet<Guid>?> GetAuthorizedEmployeeIdsAsync(
        Guid companyId, Guid callerEmployeeId, CancellationToken cancellationToken)
    {
        if (await IsHrAdministratorAsync(callerEmployeeId, cancellationToken))
            return null;

        var descendantIds = await _directReportsReader.GetAllDescendantIdsAsync(
            companyId, callerEmployeeId, cancellationToken);

        return descendantIds.ToHashSet();
    }

    /// <summary>
    /// Single-resource authorization: HR Administrator, or a manager anywhere above the target
    /// employee in the full reporting hierarchy (direct or indirect), may view. Used for
    /// individual return-to-work review reads, where the review id is a route value that must
    /// not be guessable/enumerable by an unrelated manager. Self-access is deliberately excluded
    /// here — these endpoints are manager/HR-review views, not self-service.
    /// </summary>
    public Task<bool> CanViewEmployeeAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            companyId, companyId, callerEmployeeId, targetEmployeeId, cancellationToken, allowSelf: false);
}
