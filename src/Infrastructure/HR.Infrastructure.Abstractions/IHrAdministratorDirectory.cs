namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Cross-module read surface for resolving which employees hold the HR Administrator role for a
/// company — used by modules that need to notify HR admins about something (e.g. Support ticket
/// status changes) without referencing HR.Modules.Identity directly. Implemented in the Identity
/// module (the schema owner for roles/user profiles) and DI-registered there.
/// </summary>
public interface IHrAdministratorDirectory
{
    /// <summary>
    /// Employee ids (== UserProfile ids, by this codebase's established employee/user id
    /// convention) of every active HR Administrator for the given company.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetHrAdministratorEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken);
}
