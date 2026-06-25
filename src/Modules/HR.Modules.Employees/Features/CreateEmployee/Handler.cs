using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateEmployee;

internal sealed class CreateEmployeeHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IIntegrationEventPublisher _publisher;

    public CreateEmployeeHandler(EmployeesDbContext dbContext, IClock clock, IIntegrationEventPublisher publisher)
    {
        _dbContext = dbContext;
        _clock = clock;
        _publisher = publisher;
    }

    public async Task<Result<CreateEmployeeResponse>> HandleAsync(
        CreateEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.WorkEmail.Trim().ToLowerInvariant();

        var emailExists = await _dbContext.Employees
            .AnyAsync(
                e => e.CompanyId == request.CompanyId &&
                     e.WorkEmail == normalizedEmail,
                cancellationToken);

        if (emailExists)
        {
            return Result.Failure<CreateEmployeeResponse>(
                Error.Conflict($"An employee with work email '{request.WorkEmail.Trim()}' already exists in this company."));
        }

        if (request.DepartmentId is not null)
        {
            var departmentExists = await _dbContext.Departments
                .AnyAsync(
                    d => d.Id == request.DepartmentId &&
                         d.CompanyId == request.CompanyId &&
                         d.IsActive,
                    cancellationToken);

            if (!departmentExists)
            {
                return Result.Failure<CreateEmployeeResponse>(
                    Error.NotFound($"Department '{request.DepartmentId}' was not found."));
            }
        }

        if (request.PositionProfileId is not null)
        {
            var positionProfileExists = await _dbContext.PositionProfiles
                .AnyAsync(
                    p => p.Id == request.PositionProfileId &&
                         p.CompanyId == request.CompanyId &&
                         p.IsActive,
                    cancellationToken);

            if (!positionProfileExists)
            {
                return Result.Failure<CreateEmployeeResponse>(
                    Error.NotFound($"Position profile '{request.PositionProfileId}' was not found."));
            }
        }

        if (request.ManagerId is not null)
        {
            var managerExists = await _dbContext.Employees
                .AnyAsync(
                    e => e.Id == request.ManagerId &&
                         e.CompanyId == request.CompanyId &&
                         e.Status != EmploymentStatus.Terminated,
                    cancellationToken);

            if (!managerExists)
            {
                return Result.Failure<CreateEmployeeResponse>(
                    Error.NotFound($"Manager employee '{request.ManagerId}' was not found."));
            }
        }

        var now = _clock.UtcNowOffset();

        var firstName = request.FirstName.Trim();
        var lastName  = request.LastName.Trim();

        var employee = Employee.Create(
            request.Id ?? Guid.NewGuid(),
            request.CompanyId,
            firstName,
            lastName,
            normalizedEmail,
            request.StartDate,
            request.HasSystemAccess,
            now);

        var preferredName = string.IsNullOrWhiteSpace(request.PreferredName)
            ? firstName
            : request.PreferredName.Trim();

        employee.UpdatePersonalDetails(
            preferredName,
            request.DateOfBirth,
            request.Nationality.Trim(),
            request.Gender.Trim(),
            string.IsNullOrWhiteSpace(request.GenderOther) ? null : request.GenderOther.Trim(),
            now);

        employee.UpdateContactDetails(
            string.IsNullOrWhiteSpace(request.PersonalEmail) ? null : request.PersonalEmail.Trim(),
            request.PhoneNumber,
            request.HomePhone,
            request.AddressLine1,
            request.AddressLine2,
            request.City,
            request.County,
            request.PostCode,
            request.Country,
            now);

        if (request.DepartmentId is not null || request.PositionProfileId is not null || request.ManagerId is not null)
        {
            employee.Assign(request.DepartmentId, request.PositionProfileId, request.ManagerId, now);
        }

        _dbContext.Employees.Add(employee);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _publisher.PublishAsync(new EmployeeCreatedIntegrationEvent(employee.CompanyId, employee.Id, employee.StartDate, employee.ManagerId), cancellationToken);

        return Result.Success(new CreateEmployeeResponse(
            employee.Id,
            employee.CompanyId,
            employee.DepartmentId,
            employee.PositionProfileId,
            employee.ManagerId,
            employee.FirstName,
            employee.LastName,
            employee.WorkEmail,
            employee.PersonalEmail,
            employee.StartDate,
            employee.Status,
            employee.HasSystemAccess,
            employee.CreatedAt));
    }
}
