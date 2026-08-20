namespace HR.Modules.Employees.Contracts;

public sealed record ImportLookupResult(Guid Id, bool WasCreated);

public sealed record PositionProfileImportLookupResult(Guid? Id, bool WasCreated, bool Skipped);

public interface IImportLookupResolver
{
    Task<ImportLookupResult> GetOrCreateDepartmentAsync(Guid companyId, string name, CancellationToken cancellationToken);

    Task<ImportLookupResult> GetOrCreateEmploymentTypeAsync(Guid companyId, string name, CancellationToken cancellationToken);

    Task<ImportLookupResult> GetOrCreateLocationAsync(Guid companyId, string name, CancellationToken cancellationToken);

    Task<PositionProfileImportLookupResult> GetOrCreatePositionProfileAsync(
        Guid companyId, string title, Guid? departmentId, Guid? locationId, CancellationToken cancellationToken);

    /// <summary>
    /// Read-only existence check used during import preview/validation: does NOT create anything.
    /// Returns the existing id, or null if a name/title matching this value does not yet exist for
    /// the company (in which case it will be created later, at ConfirmImportSession time, via the
    /// corresponding GetOrCreate* method).
    /// </summary>
    Task<Guid?> TryFindDepartmentAsync(Guid companyId, string name, CancellationToken cancellationToken);

    Task<Guid?> TryFindEmploymentTypeAsync(Guid companyId, string name, CancellationToken cancellationToken);

    Task<Guid?> TryFindLocationAsync(Guid companyId, string name, CancellationToken cancellationToken);

    Task<Guid?> TryFindPositionProfileAsync(Guid companyId, string title, CancellationToken cancellationToken);
}
