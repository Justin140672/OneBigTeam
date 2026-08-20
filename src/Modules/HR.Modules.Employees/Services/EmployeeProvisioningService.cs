using HR.Modules.Employees.Features.CreateEmployee;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeeProvisioningService(
    CreateEmployeeHandler createEmployeeHandler,
    EmployeesDbContext dbContext,
    IClock clock) : IEmployeeProvisioningService
{
    public async Task<Result<Guid>> CreateFromCandidateAsync(
        EmployeeProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        var result = await createEmployeeHandler.HandleAsync(
            new CreateEmployeeRequest
            {
                CompanyId         = request.CompanyId,
                DepartmentId      = request.DepartmentId,
                LocationId        = request.LocationId,
                PositionProfileId = request.PositionProfileId,
                ManagerId         = request.ManagerId,
                FirstName         = request.FirstName,
                LastName          = request.LastName,
                WorkEmail         = request.WorkEmail,
                PersonalEmail     = request.PersonalEmail,
                StartDate         = request.StartDate,
                DateOfBirth       = request.DateOfBirth,
                Nationality       = request.Nationality,
                Gender            = request.Gender,
                GenderOther       = request.GenderOther,
                PhoneNumber       = request.PhoneNumber,
                EmployeeNumber    = request.EmployeeNumber,
                EmploymentTypeId  = request.EmploymentTypeId,
            },
            cancellationToken);

        return result.IsSuccess
            ? Result.Success(result.Value!.Id)
            : Result.Failure<Guid>(result.Error);
    }

    public async Task MarkAsInitialCompanyAdminAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .SingleOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, cancellationToken);

        if (employee is null)
            return;

        employee.MarkAsInitialCompanyAdmin(clock.UtcNowOffset());
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
