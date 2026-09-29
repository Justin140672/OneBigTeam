using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeeApplicantReader(EmployeesDbContext dbContext) : IEmployeeApplicantReader
{
    public async Task<EmployeeApplicantProfile?> GetApplicantAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Id == employeeId)
            .Select(e => new { e.Id, e.CompanyId, e.FirstName, e.LastName, e.WorkEmail, e.PhoneNumber, e.Status })
            .SingleOrDefaultAsync(cancellationToken);

        if (employee is null)
            return null;

        return new EmployeeApplicantProfile(
            employee.Id,
            employee.CompanyId,
            employee.FirstName,
            employee.LastName,
            employee.WorkEmail,
            employee.PhoneNumber,
            MapState(employee.Status));
    }

    internal static EmployeeApplicantEmploymentState MapState(EmploymentStatus status) => status switch
    {
        EmploymentStatus.Active => EmployeeApplicantEmploymentState.Active,
        EmploymentStatus.Suspended => EmployeeApplicantEmploymentState.Suspended,
        EmploymentStatus.Leaving => EmployeeApplicantEmploymentState.Leaving,
        EmploymentStatus.FormerEmployee => EmployeeApplicantEmploymentState.Former,
        _ => EmployeeApplicantEmploymentState.Draft,
    };
}
