using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Authorization;

namespace HR.Modules.Tasks.Services;

/// <summary>
/// Resource-level (self / manager-hierarchy / HR administrator) authorization for Tasks
/// endpoints that resolve a specific task or a specific employee's task list. Endpoint-level
/// Policies("role:employee") only proves the caller holds a role — it never proves the caller
/// has a relationship to the specific task's assignee, so that check lives here and must be
/// applied before/around the handler runs. Standardised on the shared IAM-07 evaluation order by
/// HR.SharedKernel.Authorization.EmployeeResourceAuthorizer — this class replaces the inline
/// self/hierarchy/HR-admin logic that used to live directly in CompleteTaskHandler (SEC-003) and
/// mirrors HR.Modules.Documents.Services.DocumentResourceAuthorizer /
/// HR.Modules.Leave.Services.LeaveResourceAuthorizer /
/// HR.Modules.Sickness.Services.SicknessResourceAuthorizer /
/// HR.Modules.Probation.Services.ProbationResourceAuthorizer.
/// </summary>
internal sealed class TasksResourceAuthorizer
{
    // Mirrors HR.Modules.Identity.Domain.SystemRoles.HrAdministrator. Tasks cannot reference
    // Identity's internal SystemRoles directly, so the role id is duplicated here as the
    // sanctioned escape hatch — same pattern as HR.Modules.Leave.Services.LeaveResourceAuthorizer
    // and HR.Modules.Probation.Services.ProbationResourceAuthorizer's copy.
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    private readonly IAuthorizationService _authorizationService;
    private readonly EmployeeResourceAuthorizer _resourceAuthorizer;

    public TasksResourceAuthorizer(
        IAuthorizationService authorizationService,
        IDirectReportsReader directReportsReader)
    {
        _authorizationService = authorizationService;
        _resourceAuthorizer = new EmployeeResourceAuthorizer(
            IsHrAdministratorAsync,
            directReportsReader.GetAllDescendantIdsAsync);
    }

    public async Task<bool> IsHrAdministratorAsync(Guid callerEmployeeId, CancellationToken cancellationToken)
        => (await _authorizationService.GetEffectiveRolesAsync(callerEmployeeId, cancellationToken))
            .Contains(HrAdministratorRoleId);

    /// <summary>
    /// Assignee, any manager in the assignee's complete reporting hierarchy (direct or
    /// indirect), or an HR Administrator may view or complete a task assigned to
    /// <paramref name="targetEmployeeId"/>. Used to gate GetTask, GetEmployeeTasks and
    /// CompleteTask alike. <paramref name="targetEmployeeId"/> should be the task's effective
    /// assignee (AssignedEmployeeId ?? AssignedUserId, per CompleteTaskHandler's convention) —
    /// callers with an unassigned task have no self/hierarchy path and must rely on the
    /// HR-administrator override alone.
    /// </summary>
    public Task<bool> CanAccessEmployeeTasksAsync(
        Guid companyId, Guid callerEmployeeId, Guid targetEmployeeId, CancellationToken cancellationToken)
        => _resourceAuthorizer.CanAccessAsync(
            companyId, companyId, callerEmployeeId, targetEmployeeId, cancellationToken);
}
