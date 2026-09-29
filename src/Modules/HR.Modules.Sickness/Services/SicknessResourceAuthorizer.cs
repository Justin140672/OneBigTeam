using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Authorization;

namespace HR.Modules.Sickness.Services;

internal sealed class SicknessResourceAuthorizer
{
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

    public Task<bool> CanViewManagerTeamAsync(
        Guid companyId, Guid callerEmployeeId, Guid managerId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            companyId, companyId, callerEmployeeId, managerId, cancellationToken);
}
