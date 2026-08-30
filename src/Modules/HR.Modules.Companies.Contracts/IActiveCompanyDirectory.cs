namespace HR.Modules.Companies.Contracts;

/// <summary>
/// Cross-module contract (owned by HR.Modules.Companies) exposing the set of currently-active
/// company ids. Used by platform-wide recurring jobs that must fan out over every tenant — e.g.
/// ADM-03's daily compliance-alert scan in HR.Modules.Reporting — without taking a direct
/// reference to the Companies implementation project.
/// </summary>
public interface IActiveCompanyDirectory
{
    Task<IReadOnlyList<Guid>> GetActiveCompanyIdsAsync(CancellationToken cancellationToken);
}
