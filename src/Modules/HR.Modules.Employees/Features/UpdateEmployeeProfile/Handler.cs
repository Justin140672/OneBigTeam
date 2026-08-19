using System.Text.RegularExpressions;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.UpdateEmployeeProfile;

internal sealed class UpdateEmployeeProfileHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ICompanyContactValidationReader _contactValidationReader;
    private readonly IAuditEventPublisher _auditEventPublisher;
    private readonly IIntegrationEventPublisher _integrationEventPublisher;

    public UpdateEmployeeProfileHandler(
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

    public async Task<Result<UpdateEmployeeProfileResponse>> HandleAsync(
        UpdateEmployeeProfileRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var contactRules = await _contactValidationReader.GetContactValidationRulesAsync(request.CompanyId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.PostCode) &&
            !Regex.IsMatch(request.PostCode.Trim(), contactRules.PostcodeRegex, RegexOptions.IgnoreCase))
            return Result.Failure<UpdateEmployeeProfileResponse>(Error.Validation($"'{request.PostCode.Trim()}' is not a valid postcode."));

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) &&
            !Regex.IsMatch(request.PhoneNumber.Trim(), contactRules.MobileRegex, RegexOptions.IgnoreCase))
            return Result.Failure<UpdateEmployeeProfileResponse>(Error.Validation($"'{request.PhoneNumber.Trim()}' is not a valid mobile number."));

        if (!string.IsNullOrWhiteSpace(request.HomePhone) &&
            !Regex.IsMatch(request.HomePhone.Trim(), contactRules.TelephoneRegex, RegexOptions.IgnoreCase))
            return Result.Failure<UpdateEmployeeProfileResponse>(Error.Validation($"'{request.HomePhone.Trim()}' is not a valid phone number."));

        var employee = await _dbContext.Employees
            .SingleOrDefaultAsync(
                e => e.Id == request.Id && e.CompanyId == request.CompanyId,
                cancellationToken);

        if (employee is null)
        {
            return Result.Failure<UpdateEmployeeProfileResponse>(
                Error.NotFound($"Employee with id '{request.Id}' was not found."));
        }

        var newEmail = request.WorkEmail.Trim().ToLowerInvariant();

        if (!string.Equals(employee.WorkEmail, newEmail, StringComparison.Ordinal))
        {
            var emailTaken = await _dbContext.Employees
                .AnyAsync(
                    e => e.CompanyId == request.CompanyId &&
                         e.Id != request.Id &&
                         e.WorkEmail == newEmail,
                    cancellationToken);

            if (emailTaken)
            {
                return Result.Failure<UpdateEmployeeProfileResponse>(
                    Error.Conflict($"An employee with work email '{request.WorkEmail.Trim()}' already exists in this company."));
            }
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

        employee.UpdateProfile(
            request.FirstName.Trim(),
            request.LastName.Trim(),
            newEmail,
            string.IsNullOrWhiteSpace(request.PersonalEmail) ? null : request.PersonalEmail.Trim(),
            request.StartDate,
            now);

        employee.UpdatePersonalDetails(
            request.PreferredName,
            request.DateOfBirth ?? employee.DateOfBirth,
            request.Nationality ?? employee.Nationality,
            request.Gender ?? employee.Gender,
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

        employee.Assign(
            request.DepartmentId ?? employee.DepartmentId,
            request.PositionProfileId ?? employee.PositionProfileId,
            request.LocationId ?? employee.LocationId,
            employee.ManagerId,
            now);
        employee.SetSystemAccess(request.HasSystemAccess, now);
        employee.SetWorkingPattern(request.WorkingDaysOverride, request.HoursPerDayOverride, now);

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
            new EmployeeProfileUpdatedAuditEvent(employee.CompanyId, employee.Id, actorEmployeeId, now, before, after, request.CorrelationId),
            cancellationToken);

        // Timeline granularity: publish a specific event only for the field that actually
        // changed rather than a bundled "profile updated" for everything. Manager is not
        // touched by this feature (Assign is called with the employee's existing ManagerId), so
        // only Position/Location are checked here alongside a catch-all "details corrected" for
        // anything else that changed (name, email, personal details, etc.) — kept deliberately
        // generic per EmployeeDetailsCorrectedIntegrationEvent's doc comment.
        if (before.PositionProfileId != after.PositionProfileId && after.PositionProfileId is not null && before.PositionProfileId is not null)
        {
            await _integrationEventPublisher.PublishAsync(
                new EmployeePositionChangedIntegrationEvent(
                    employee.CompanyId, employee.Id, before.PositionProfileId.Value, after.PositionProfileId.Value, now),
                cancellationToken);
        }

        if (before.LocationId != after.LocationId && after.LocationId is not null && before.LocationId is not null)
        {
            await _integrationEventPublisher.PublishAsync(
                new EmployeeLocationChangedIntegrationEvent(
                    employee.CompanyId, employee.Id, before.LocationId.Value, after.LocationId.Value, now),
                cancellationToken);
        }

        var otherFieldsChanged =
            before.FirstName != after.FirstName ||
            before.LastName != after.LastName ||
            before.WorkEmail != after.WorkEmail ||
            before.PersonalEmail != after.PersonalEmail ||
            before.StartDate != after.StartDate ||
            before.PreferredName != after.PreferredName ||
            before.DateOfBirth != after.DateOfBirth ||
            before.Nationality != after.Nationality ||
            before.Gender != after.Gender ||
            before.GenderOther != after.GenderOther ||
            before.DepartmentId != after.DepartmentId ||
            before.HasSystemAccess != after.HasSystemAccess;

        if (otherFieldsChanged)
        {
            await _integrationEventPublisher.PublishAsync(
                new EmployeeDetailsCorrectedIntegrationEvent(employee.CompanyId, employee.Id, now),
                cancellationToken);
        }

        return Result.Success(new UpdateEmployeeProfileResponse(
            employee.Id,
            employee.CompanyId,
            employee.DepartmentId,
            employee.LocationId,
            employee.FirstName,
            employee.LastName,
            employee.WorkEmail,
            employee.PersonalEmail,
            employee.StartDate,
            employee.Status,
            employee.HasSystemAccess,
            employee.UpdatedAt));
    }
}
