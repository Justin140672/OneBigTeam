using System.Text.RegularExpressions;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateEmployee;

internal sealed class CreateEmployeeHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IProbationDateResolver _probationDateResolver;
    private readonly ICompanyContactValidationReader _contactValidationReader;

    public CreateEmployeeHandler(
        EmployeesDbContext dbContext,
        IClock clock,
        IIntegrationEventPublisher publisher,
        IProbationDateResolver probationDateResolver,
        ICompanyContactValidationReader contactValidationReader)
    {
        _dbContext = dbContext;
        _clock = clock;
        _publisher = publisher;
        _probationDateResolver = probationDateResolver;
        _contactValidationReader = contactValidationReader;
    }

    public async Task<Result<CreateEmployeeResponse>> HandleAsync(
        CreateEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var contactRules = await _contactValidationReader.GetContactValidationRulesAsync(request.CompanyId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.PostCode) &&
            !Regex.IsMatch(request.PostCode.Trim(), contactRules.PostcodeRegex, RegexOptions.IgnoreCase))
            return Result.Failure<CreateEmployeeResponse>(Error.Validation($"'{request.PostCode.Trim()}' is not a valid postcode."));

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) &&
            !Regex.IsMatch(request.PhoneNumber.Trim(), contactRules.MobileRegex, RegexOptions.IgnoreCase))
            return Result.Failure<CreateEmployeeResponse>(Error.Validation($"'{request.PhoneNumber.Trim()}' is not a valid mobile number."));

        if (!string.IsNullOrWhiteSpace(request.HomePhone) &&
            !Regex.IsMatch(request.HomePhone.Trim(), contactRules.TelephoneRegex, RegexOptions.IgnoreCase))
            return Result.Failure<CreateEmployeeResponse>(Error.Validation($"'{request.HomePhone.Trim()}' is not a valid phone number."));

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

        PositionProfile? positionProfile = null;
        if (request.PositionProfileId is not null)
        {
            positionProfile = await _dbContext.PositionProfiles
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    p => p.Id == request.PositionProfileId &&
                         p.CompanyId == request.CompanyId &&
                         p.IsActive,
                    cancellationToken);

            if (positionProfile is null)
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

        var probationEndDate = await _probationDateResolver.ResolveEndDateAsync(
            request.CompanyId, positionProfile?.ProbationMonthsOverride, employee.StartDate, cancellationToken);
        employee.SetProbationEndDate(probationEndDate, now);

        _dbContext.Employees.Add(employee);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _publisher.PublishAsync(new EmployeeCreatedIntegrationEvent(employee.CompanyId, employee.Id, employee.StartDate, employee.ManagerId, probationEndDate, employee.PositionProfileId), cancellationToken);

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
