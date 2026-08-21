using System.Text.RegularExpressions;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CompleteInitialEmployeeSetup;

internal sealed class CompleteInitialEmployeeSetupHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ICompanyContactValidationReader _contactValidationReader;
    private readonly IAuditEventPublisher _auditEventPublisher;
    private readonly IIntegrationEventPublisher _integrationEventPublisher;

    public CompleteInitialEmployeeSetupHandler(
        EmployeesDbContext dbContext,
        IClock clock,
        ICompanyContactValidationReader contactValidationReader,
        IAuditEventPublisher auditEventPublisher,
        IIntegrationEventPublisher integrationEventPublisher)
    {
        _dbContext = dbContext;
        _clock = clock;
        _contactValidationReader = contactValidationReader;
        _auditEventPublisher = auditEventPublisher;
        _integrationEventPublisher = integrationEventPublisher;
    }

    public async Task<Result<CompleteInitialEmployeeSetupResponse>> HandleAsync(
        CompleteInitialEmployeeSetupRequest request,
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .SingleOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, cancellationToken);

        if (employee is null)
        {
            return Result.Failure<CompleteInitialEmployeeSetupResponse>(
                Error.NotFound($"Employee with id '{employeeId}' was not found."));
        }

        if (!employee.RequiresInitialSetup)
        {
            return Result.Failure<CompleteInitialEmployeeSetupResponse>(
                Error.Conflict("Initial employee setup has already been completed."));
        }

        var contactRules = await _contactValidationReader.GetContactValidationRulesAsync(companyId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.PostCode) &&
            !Regex.IsMatch(request.PostCode.Trim(), contactRules.PostcodeRegex, RegexOptions.IgnoreCase))
            return Result.Failure<CompleteInitialEmployeeSetupResponse>(Error.Validation($"'{request.PostCode.Trim()}' is not a valid postcode."));

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) &&
            !Regex.IsMatch(request.PhoneNumber.Trim(), contactRules.MobileRegex, RegexOptions.IgnoreCase))
            return Result.Failure<CompleteInitialEmployeeSetupResponse>(Error.Validation($"'{request.PhoneNumber.Trim()}' is not a valid mobile number."));

        if (!string.IsNullOrWhiteSpace(request.HomePhone) &&
            !Regex.IsMatch(request.HomePhone.Trim(), contactRules.TelephoneRegex, RegexOptions.IgnoreCase))
            return Result.Failure<CompleteInitialEmployeeSetupResponse>(Error.Validation($"'{request.HomePhone.Trim()}' is not a valid phone number."));

        var hasCompensation = await _dbContext.Compensations
            .AnyAsync(c => c.EmployeeId == employee.Id, cancellationToken);

        if (!hasCompensation)
        {
            return Result.Failure<CompleteInitialEmployeeSetupResponse>(
                Error.Validation("At least one compensation record must exist for this employee before initial setup can be completed."));
        }

        var now = _clock.UtcNowOffset();

        var before = new EmployeeProfileSnapshot(
            employee.FirstName,
            employee.LastName,
            employee.WorkEmail,
            employee.PersonalEmail,
            employee.StartDate,
            employee.PreferredName,
            employee.DateOfBirth,
            employee.Nationality,
            employee.Gender,
            employee.GenderOther,
            employee.DepartmentId,
            employee.PositionProfileId,
            employee.LocationId,
            employee.HasSystemAccess);

        employee.UpdatePersonalDetails(
            request.PreferredName,
            request.DateOfBirth,
            request.Nationality,
            request.Gender,
            request.GenderOther,
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

        employee.UpdateProfile(
            request.FirstName.Trim(),
            request.LastName.Trim(),
            employee.WorkEmail,
            string.IsNullOrWhiteSpace(request.PersonalEmail) ? null : request.PersonalEmail.Trim(),
            employee.StartDate,
            now);

        employee.CompleteInitialSetup(now);
        employee.Activate(now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var after = new EmployeeProfileSnapshot(
            employee.FirstName,
            employee.LastName,
            employee.WorkEmail,
            employee.PersonalEmail,
            employee.StartDate,
            employee.PreferredName,
            employee.DateOfBirth,
            employee.Nationality,
            employee.Gender,
            employee.GenderOther,
            employee.DepartmentId,
            employee.PositionProfileId,
            employee.LocationId,
            employee.HasSystemAccess);

        await _auditEventPublisher.PublishAsync(
            new EmployeeProfileUpdatedAuditEvent(employee.CompanyId, employee.Id, employeeId, now, before, after, CorrelationId: null),
            cancellationToken);

        await _integrationEventPublisher.PublishAsync(
            new EmployeeDetailsCorrectedIntegrationEvent(employee.CompanyId, employee.Id, now),
            cancellationToken);

        return Result.Success(new CompleteInitialEmployeeSetupResponse(
            employee.Id,
            employee.RequiresInitialSetup,
            employee.Status));
    }
}
