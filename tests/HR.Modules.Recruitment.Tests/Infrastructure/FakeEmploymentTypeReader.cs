using HR.Modules.Employees.Contracts;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal sealed class FakeEmploymentTypeReader : IEmploymentTypeReader
{
    private readonly HashSet<(Guid CompanyId, Guid Id)>? _active;

    private FakeEmploymentTypeReader(HashSet<(Guid CompanyId, Guid Id)>? active) => _active = active;

    public static FakeEmploymentTypeReader Permissive() => new(null);

    public static FakeEmploymentTypeReader Active(params (Guid CompanyId, Guid Id)[] active) => new([.. active]);

    public Task<bool> IsActiveAsync(Guid companyId, Guid employmentTypeId, CancellationToken cancellationToken) =>
        Task.FromResult(_active is null || _active.Contains((companyId, employmentTypeId)));
}
