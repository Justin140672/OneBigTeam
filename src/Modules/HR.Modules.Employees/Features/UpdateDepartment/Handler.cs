using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.UpdateDepartment;

internal sealed class UpdateDepartmentHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;

    public UpdateDepartmentHandler(EmployeesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<UpdateDepartmentResponse>> HandleAsync(
        UpdateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var department = await _dbContext.Departments
            .SingleOrDefaultAsync(
                d => d.Id == request.Id && d.CompanyId == request.CompanyId && d.IsActive,
                cancellationToken);

        if (department is null)
        {
            return Result.Failure<UpdateDepartmentResponse>(
                Error.NotFound($"Department '{request.Id}' was not found."));
        }

        // Validate parent exists in same company (if changing parent)
        if (request.ParentDepartmentId is not null && request.ParentDepartmentId != department.ParentDepartmentId)
        {
            if (request.ParentDepartmentId == request.Id)
            {
                return Result.Failure<UpdateDepartmentResponse>(
                    Error.Validation("A department cannot be its own parent."));
            }

            var parentExists = await _dbContext.Departments
                .AnyAsync(
                    d => d.Id == request.ParentDepartmentId &&
                         d.CompanyId == request.CompanyId &&
                         d.IsActive,
                    cancellationToken);

            if (!parentExists)
            {
                return Result.Failure<UpdateDepartmentResponse>(
                    Error.NotFound($"Parent department '{request.ParentDepartmentId}' was not found."));
            }
        }

        // Validate name uniqueness (excluding self), case-insensitively.
        var newName = request.Name.Trim();
        if (!string.Equals(department.Name, newName, StringComparison.OrdinalIgnoreCase))
        {
            var nameExists = await _dbContext.Departments
                .AnyAsync(
                    d => d.CompanyId == request.CompanyId &&
                         d.Id != request.Id &&
                         d.Name.ToLower() == newName.ToLower() &&
                         d.IsActive,
                    cancellationToken);

            if (nameExists)
            {
                return Result.Failure<UpdateDepartmentResponse>(
                    Error.Conflict($"An active department named '{newName}' already exists in this company."));
            }
        }

        var now = _clock.UtcNowOffset();

        department.Update(
            newName,
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.ParentDepartmentId,
            request.ManagerEmployeeId,
            now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateDepartmentResponse(
            department.Id,
            department.CompanyId,
            department.Name,
            department.Description,
            department.ParentDepartmentId,
            department.ManagerEmployeeId,
            department.IsActive,
            department.UpdatedAt));
    }
}
