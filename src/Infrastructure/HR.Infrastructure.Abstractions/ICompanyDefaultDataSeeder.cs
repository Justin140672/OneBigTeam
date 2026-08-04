namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Sanctioned cross-module contract, implemented internally in HR.Modules.Employees, that seeds
/// the minimum set of default setup data (Department/Location/PositionProfile/EmploymentType) a
/// brand-new company needs before its first Employee record can be created — the existing
/// CreateEmployeeHandler validator requires all four ids. Consumed by HR.Modules.Identity's
/// self-service SignUp feature without Identity ever referencing HR.Modules.Employees directly.
/// </summary>
public interface ICompanyDefaultDataSeeder
{
    Task<CompanyDefaultDataResult> SeedDefaultsAsync(Guid companyId, CancellationToken cancellationToken);
}

public sealed record CompanyDefaultDataResult(
    Guid DepartmentId,
    Guid LocationId,
    Guid PositionProfileId,
    Guid EmploymentTypeId);
