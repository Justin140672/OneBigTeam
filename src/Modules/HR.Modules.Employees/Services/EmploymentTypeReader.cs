using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmploymentTypeReader(EmployeesDbContext dbContext) : IEmploymentTypeReader
{
    public Task<bool> IsActiveAsync(Guid companyId, Guid employmentTypeId, CancellationToken cancellationToken) =>
        dbContext.EmploymentTypes
            .AsNoTracking()
            .AnyAsync(
                t => t.Id == employmentTypeId && t.CompanyId == companyId && t.IsActive,
                cancellationToken);
}
