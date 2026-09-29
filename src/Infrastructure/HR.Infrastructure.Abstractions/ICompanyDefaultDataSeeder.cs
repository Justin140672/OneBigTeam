namespace HR.Infrastructure.Abstractions;

public interface ICompanyDefaultDataSeeder
{
    Task<CompanyDefaultDataResult> SeedDefaultsAsync(Guid companyId, CancellationToken cancellationToken);
}

public sealed record CompanyDefaultDataResult(
    Guid DepartmentId,
    Guid LocationId,
    Guid PositionProfileId,
    Guid EmploymentTypeId);
