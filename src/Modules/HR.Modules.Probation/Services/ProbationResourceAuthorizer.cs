using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Authorization;

namespace HR.Modules.Probation.Services;

internal sealed class ProbationResourceAuthorizer
{
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    private readonly IAuthorizationService _authorizationService;
    private readonly IDirectReportsReader _directReportsReader;
    private readonly EmployeeResourceAuthorizer _resourceAuthorizer;

    public ProbationResourceAuthorizer(
        IAuthorizationService authorizationService,
        IDirectReportsReader directReportsReader)
    {
        _authorizationService = authorizationService;
        _directReportsReader = directReportsReader;
        _resourceAuthorizer = new EmployeeResourceAuthorizer(
            IsHrAdministratorAsync,
            directReportsReader.GetAllDescendantIdsAsync);
    }

    public async Task<bool> IsHrAdministratorAsync(Guid callerEmployeeId, CancellationToken cancellationToken)
        => (await _authorizationService.GetEffectiveRolesAsync(callerEmployeeId, cancellationToken))
            .Contains(HrAdministratorRoleId);

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
    /// individual probation review reads, where the review id is a route value that must not be
    /// guessable/enumerable by an unrelated manager. Self-access is deliberately excluded here —
    /// these endpoints are manager/HR-review views, not self-service.
    /// </summary>
    public Task<bool> CanViewEmployeeAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            companyId, companyId, callerEmployeeId, targetEmployeeId, cancellationToken, allowSelf: false);
}
