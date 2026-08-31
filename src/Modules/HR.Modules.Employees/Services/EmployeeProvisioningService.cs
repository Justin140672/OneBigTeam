using HR.Modules.Employees.Features.CreateEmployee;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
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
                AddressLine1      = request.AddressLine1,
                AddressLine2      = request.AddressLine2,
                City              = request.City,
                County            = request.County,
                PostCode          = request.PostCode,
                SourceReference   = request.SourceReference,
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

        var now = clock.UtcNowOffset();
        employee.MarkAsInitialCompanyAdmin(now);

        // The initial admin's employee record is created with placeholder personal details (see
        // SignUpHandler.CreateAdminEmployeeAsync) — flag it as requiring the first-login "Complete
        // your employee profile" flow so HR.Web can block normal access until the real details (and
        // at least one compensation record) are entered. Only ever set for this specific record.
        employee.MarkRequiresInitialSetup(now);

        // CompleteInitialEmployeeSetupHandler requires at least one compensation record to exist
        // before initial setup can be completed, but salary is deliberately NOT one of the fields
        // the first-login dialog collects (same reasoning as Department/Location/Position/
        // EmploymentType being out of scope there) — seed a zero-value placeholder here, alongside
        // the placeholder DateOfBirth/Nationality/Gender already set by
        // SignUpHandler.CreateAdminEmployeeAsync, for an HR admin to correct later via the normal
        // Compensation screens.
        var hasCompensation = await dbContext.Compensations
            .AnyAsync(c => c.EmployeeId == employeeId, cancellationToken);

        if (!hasCompensation)
        {
            var compensation = Compensation.Create(
                Guid.NewGuid(),
                companyId,
                employeeId,
                employee.StartDate,
                SalaryType.Annual,
                salary: 0m,
                currency: "GBP",
                hoursPerWeek: null,
                fte: null,
                notes: "Placeholder compensation created during company signup — review and update.",
                CompensationChangeReason.NewHire,
                createdBy: employeeId,
                now);

            dbContext.Compensations.Add(compensation);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
