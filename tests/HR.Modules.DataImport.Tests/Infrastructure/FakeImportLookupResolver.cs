using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.DataImport.Tests.Infrastructure;

internal sealed class FakeImportLookupResolver : IImportLookupResolver
{
    private readonly Dictionary<(Guid CompanyId, string NormalizedName), Guid> _departments = new();
    private readonly Dictionary<(Guid CompanyId, string NormalizedName), Guid> _employmentTypes = new();
    private readonly Dictionary<(Guid CompanyId, string NormalizedName), Guid> _locations = new();
    private readonly Dictionary<(Guid CompanyId, string NormalizedName), List<Guid>> _positionProfiles = new();

    public void SeedExistingDepartment(Guid companyId, string name, Guid id) =>
        _departments[Key(companyId, name)] = id;

    public void SeedExistingEmploymentType(Guid companyId, string name, Guid id) =>
        _employmentTypes[Key(companyId, name)] = id;

    public void SeedExistingLocation(Guid companyId, string name, Guid id) =>
        _locations[Key(companyId, name)] = id;

    public void SeedExistingPositionProfile(Guid companyId, string title, Guid id) =>
        AddProfile(Key(companyId, title), id);

    public Task<ImportLookupResult> GetOrCreateDepartmentAsync(Guid companyId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(GetOrCreate(_departments, companyId, name));

    public Task<ImportLookupResult> GetOrCreateEmploymentTypeAsync(Guid companyId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(GetOrCreate(_employmentTypes, companyId, name));

    public Task<ImportLookupResult> GetOrCreateLocationAsync(Guid companyId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(GetOrCreate(_locations, companyId, name));

    public Task<PositionProfileImportLookupResult> GetOrCreatePositionProfileAsync(
        Guid companyId, string title, Guid? departmentId, Guid? locationId,
        IReadOnlySet<Guid> excludedProfileIds, Guid? allowedEmployeeId, CancellationToken cancellationToken)
    {
        var key = Key(companyId, title);

        if (FindProfile(key, excludedProfileIds) is { } existingId)
            return Task.FromResult(new PositionProfileImportLookupResult(existingId, WasCreated: false, Skipped: false));

        if (departmentId is null || locationId is null)
            return Task.FromResult(new PositionProfileImportLookupResult(Id: null, WasCreated: false, Skipped: true));

        var newId = Guid.NewGuid();
        AddProfile(key, newId);
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

    public Task<Guid?> TryFindDepartmentAsync(Guid companyId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(_departments.TryGetValue(Key(companyId, name), out var id) ? (Guid?)id : null);

    public Task<Guid?> TryFindEmploymentTypeAsync(Guid companyId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(_employmentTypes.TryGetValue(Key(companyId, name), out var id) ? (Guid?)id : null);

    public Task<Guid?> TryFindLocationAsync(Guid companyId, string name, CancellationToken cancellationToken) =>
        Task.FromResult(_locations.TryGetValue(Key(companyId, name), out var id) ? (Guid?)id : null);

    public Task<Guid?> TryFindPositionProfileAsync(
        Guid companyId, string title, Guid? departmentId, Guid? locationId,
        IReadOnlySet<Guid> excludedProfileIds, Guid? allowedEmployeeId, CancellationToken cancellationToken) =>
        Task.FromResult(FindProfile(Key(companyId, title), excludedProfileIds));

    private static (Guid CompanyId, string NormalizedName) Key(Guid companyId, string name) =>
        (companyId, name.Trim().ToLowerInvariant());

    private void AddProfile((Guid CompanyId, string NormalizedName) key, Guid id)
    {
        if (!_positionProfiles.TryGetValue(key, out var list))
            _positionProfiles[key] = list = [];

        list.Add(id);
    }

    private Guid? FindProfile((Guid CompanyId, string NormalizedName) key, IReadOnlySet<Guid> excluded) =>
        _positionProfiles.TryGetValue(key, out var list)
            ? list.Where(id => !excluded.Contains(id)).Select(id => (Guid?)id).FirstOrDefault()
            : null;
}
