using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateDepartment;

internal sealed class CreateDepartmentHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;

    public CreateDepartmentHandler(EmployeesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<CreateDepartmentResponse>> HandleAsync(
        CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ParentDepartmentId is not null)
        {
            var parentExists = await _dbContext.Departments
                .AnyAsync(
                    d => d.Id == request.ParentDepartmentId &&
                         d.CompanyId == request.CompanyId &&
                         d.IsActive,
                    cancellationToken);

            if (!parentExists)
            {
                return Result.Failure<CreateDepartmentResponse>(
                    Error.NotFound($"Parent department '{request.ParentDepartmentId}' was not found."));
            }
        }

        var nameExists = await _dbContext.Departments
            .AnyAsync(
                d => d.CompanyId == request.CompanyId &&
                     d.Name.ToLower() == request.Name.Trim().ToLower() &&
                     d.IsActive,
                cancellationToken);

        if (nameExists)
        {
            return Result.Failure<CreateDepartmentResponse>(
                Error.Conflict($"An active department named '{request.Name.Trim()}' already exists in this company."));
        }

        var now = _clock.UtcNowOffset();

        var department = Department.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.Name.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            now);

        if (request.ParentDepartmentId is not null)
        {
            department.Update(
                department.Name,
                department.Description,
                request.ParentDepartmentId,
                department.ManagerEmployeeId,
                now);
        }

        _dbContext.Departments.Add(department);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateDepartmentResponse(
            department.Id,
            department.CompanyId,
            department.Name,
            department.Description,
            department.ParentDepartmentId,
            department.IsActive,
            department.CreatedAt));
    }
}
