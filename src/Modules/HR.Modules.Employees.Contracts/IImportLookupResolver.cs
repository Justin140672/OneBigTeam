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
}
