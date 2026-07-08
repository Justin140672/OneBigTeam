using HR.Infrastructure.Abstractions;

namespace HR.Modules.DataImport.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IImportLookupResolver"/>: lets tests seed which Department/
/// EmploymentType/Location/PositionProfile names "already exist" (mapped to a specific id) for a
/// company, while mirroring the real resolver's create-if-missing behavior for anything else -
/// auto-creating a stable new Guid per unique (companyId, normalized-name) pair on first use and
/// reusing it (with WasCreated: false) on subsequent calls, without needing a live DbContext.
/// </summary>
internal sealed class FakeImportLookupResolver : IImportLookupResolver
{
    private readonly Dictionary<(Guid CompanyId, string NormalizedName), Guid> _departments = new();
    private readonly Dictionary<(Guid CompanyId, string NormalizedName), Guid> _employmentTypes = new();
    private readonly Dictionary<(Guid CompanyId, string NormalizedName), Guid> _locations = new();
    private readonly Dictionary<(Guid CompanyId, string NormalizedName), Guid> _positionProfiles = new();

    public void SeedExistingDepartment(Guid companyId, string name, Guid id) =>
        _departments[Key(companyId, name)] = id;

    public void SeedExistingEmploymentType(Guid companyId, string name, Guid id) =>
        _employmentTypes[Key(companyId, name)] = id;

    public void SeedExistingLocation(Guid companyId, string name, Guid id) =>
        _locations[Key(companyId, name)] = id;

    public void SeedExistingPositionProfile(Guid companyId, string title, Guid id) =>
        _positionProfiles[Key(companyId, title)] = id;

    public Task<ImportLookupResult> GetOrCreateDepartmentAsync(Guid companyId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(GetOrCreate(_departments, companyId, name));

    public Task<ImportLookupResult> GetOrCreateEmploymentTypeAsync(Guid companyId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(GetOrCreate(_employmentTypes, companyId, name));

    public Task<ImportLookupResult> GetOrCreateLocationAsync(Guid companyId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(GetOrCreate(_locations, companyId, name));

    public Task<PositionProfileImportLookupResult> GetOrCreatePositionProfileAsync(
        Guid companyId, string title, Guid? departmentId, Guid? locationId, CancellationToken cancellationToken)
    {
        var key = Key(companyId, title);

        if (_positionProfiles.TryGetValue(key, out var existingId))
            return Task.FromResult(new PositionProfileImportLookupResult(existingId, WasCreated: false, Skipped: false));

        if (departmentId is null || locationId is null)
            return Task.FromResult(new PositionProfileImportLookupResult(Id: null, WasCreated: false, Skipped: true));

        var newId = Guid.NewGuid();
        _positionProfiles[key] = newId;
        return Task.FromResult(new PositionProfileImportLookupResult(newId, WasCreated: true, Skipped: false));
    }

    private static ImportLookupResult GetOrCreate(
        Dictionary<(Guid CompanyId, string NormalizedName), Guid> store, Guid companyId, string name)
    {
        var key = Key(companyId, name);

        if (store.TryGetValue(key, out var existingId))
            return new ImportLookupResult(existingId, WasCreated: false);

        var newId = Guid.NewGuid();
        store[key] = newId;
        return new ImportLookupResult(newId, WasCreated: true);
    }

    private static (Guid CompanyId, string NormalizedName) Key(Guid companyId, string name) =>
        (companyId, name.Trim().ToLowerInvariant());
}
