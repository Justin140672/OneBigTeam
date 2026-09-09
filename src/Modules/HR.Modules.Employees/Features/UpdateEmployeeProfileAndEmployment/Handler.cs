using System.Text.RegularExpressions;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.UpdateEmployeeProfileAndEmployment;

// Item 5: applies the Employee Profile tab and the Employment tab mutations to a single tracked
// Employee aggregate and commits them with one optimistic-concurrency guarded SaveChanges. A
// conflict on the shared Employee.Version rolls the whole thing back — nothing commits, so no
// audit or integration events are published for a rejected save.
internal sealed class UpdateEmployeeProfileAndEmploymentHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ICompanyContactValidationReader _contactValidationReader;
    private readonly ICompanyEmployeeNumberSettingsReader _employeeNumberSettingsReader;
    private readonly IAuditEventPublisher _auditEventPublisher;
    private readonly IIntegrationEventPublisher _integrationEventPublisher;

    public UpdateEmployeeProfileAndEmploymentHandler(
        EmployeesDbContext dbContext,
        IClock clock,
        ICompanyContactValidationReader contactValidationReader,
        ICompanyEmployeeNumberSettingsReader employeeNumberSettingsReader,
        IAuditEventPublisher auditEventPublisher,
        IIntegrationEventPublisher integrationEventPublisher)
    {
        _dbContext = dbContext;
        _clock = clock;
        _contactValidationReader = contactValidationReader;
        _employeeNumberSettingsReader = employeeNumberSettingsReader;
        _auditEventPublisher = auditEventPublisher;
        _integrationEventPublisher = integrationEventPublisher;
    }

    public async Task<Result<UpdateEmployeeProfileAndEmploymentResponse>> HandleAsync(
        UpdateEmployeeProfileAndEmploymentRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        Result<UpdateEmployeeProfileAndEmploymentResponse> Fail(Error error) =>
            Result.Failure<UpdateEmployeeProfileAndEmploymentResponse>(error);

        // ---- Profile: contact-format validation -------------------------------------------------
        var contactRules = await _contactValidationReader.GetContactValidationRulesAsync(request.CompanyId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.PostCode) &&
            !Regex.IsMatch(request.PostCode.Trim(), contactRules.PostcodeRegex, RegexOptions.IgnoreCase))
            return Fail(Error.Validation($"'{request.PostCode.Trim()}' is not a valid postcode."));

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) &&
            !Regex.IsMatch(request.PhoneNumber.Trim(), contactRules.MobileRegex, RegexOptions.IgnoreCase))
            return Fail(Error.Validation($"'{request.PhoneNumber.Trim()}' is not a valid mobile number."));

        if (!string.IsNullOrWhiteSpace(request.HomePhone) &&
            !Regex.IsMatch(request.HomePhone.Trim(), contactRules.TelephoneRegex, RegexOptions.IgnoreCase))
            return Fail(Error.Validation($"'{request.HomePhone.Trim()}' is not a valid phone number."));

        var employee = await _dbContext.Employees
            .SingleOrDefaultAsync(e => e.Id == request.Id && e.CompanyId == request.CompanyId, cancellationToken);

        if (employee is null)
            return Fail(Error.NotFound($"Employee with id '{request.Id}' was not found."));

        // ---- Profile: work email uniqueness ----------------------------------------------------
        var newEmail = request.WorkEmail.Trim().ToLowerInvariant();

        if (!string.Equals(employee.WorkEmail, newEmail, StringComparison.Ordinal))
        {
            var emailTaken = await _dbContext.Employees.AnyAsync(
                e => e.CompanyId == request.CompanyId && e.Id != request.Id && e.WorkEmail == newEmail,
                cancellationToken);

            if (emailTaken)
                return Fail(Error.Conflict($"An employee with work email '{request.WorkEmail.Trim()}' already exists in this company."));
        }

        // ---- Employment: employee number mode + uniqueness -----------------------------------
        var employeeNumberMode = await _employeeNumberSettingsReader.GetModeAsync(request.CompanyId, cancellationToken);

        var normalizedEmployeeNumber = employeeNumberMode == EmployeeNumberMode.Automatic
            ? employee.EmployeeNumber
            : request.EmployeeNumber?.Trim().ToUpperInvariant() ?? employee.EmployeeNumber;

        if (employeeNumberMode == EmployeeNumberMode.Automatic &&
            !string.IsNullOrWhiteSpace(request.EmployeeNumber) &&
            !string.Equals(request.EmployeeNumber.Trim(), employee.EmployeeNumber, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(Error.Validation("Employee number is auto-generated for this company and cannot be changed."));
        }

        if (!string.Equals(employee.EmployeeNumber, normalizedEmployeeNumber, StringComparison.Ordinal))
        {
            var employeeNumberTaken = await _dbContext.Employees.AnyAsync(
                e => e.CompanyId == request.CompanyId && e.Id != request.Id && e.EmployeeNumber == normalizedEmployeeNumber,
                cancellationToken);

            if (employeeNumberTaken)
                return Fail(Error.Conflict($"An employee with employee number '{request.EmployeeNumber}' already exists in this company."));
        }

        // ---- Employment: referenced entity existence ------------------------------------------
        if (request.DepartmentId.HasValue)
        {
            var deptExists = await _dbContext.Departments.AnyAsync(
                d => d.Id == request.DepartmentId && d.CompanyId == request.CompanyId && d.IsActive, cancellationToken);
            if (!deptExists)
                return Fail(Error.NotFound($"Department '{request.DepartmentId}' was not found or is inactive."));
        }

        if (request.LocationId.HasValue)
        {
            var locationExists = await _dbContext.Locations.AnyAsync(
                l => l.Id == request.LocationId && l.CompanyId == request.CompanyId && l.IsActive, cancellationToken);
            if (!locationExists)
                return Fail(Error.NotFound($"Location '{request.LocationId}' was not found or is inactive."));
        }

        if (request.PositionProfileId.HasValue)
        {
            var posExists = await _dbContext.PositionProfiles.AnyAsync(
                p => p.Id == request.PositionProfileId && p.CompanyId == request.CompanyId && p.IsActive, cancellationToken);
            if (!posExists)
                return Fail(Error.NotFound($"Position profile '{request.PositionProfileId}' was not found or is inactive."));
        }

        if (request.ManagerId.HasValue)
        {
            var managerExists = await _dbContext.Employees.AnyAsync(
                e => e.Id == request.ManagerId && e.CompanyId == request.CompanyId && e.Status != EmploymentStatus.FormerEmployee,
                cancellationToken);
            if (!managerExists)
                return Fail(Error.NotFound($"Manager employee '{request.ManagerId}' was not found."));

            var allEmployees = await _dbContext.Employees.AsNoTracking()
                .Where(e => e.CompanyId == request.CompanyId)
                .Select(e => new { e.Id, e.ManagerId })
                .ToDictionaryAsync(e => e.Id, e => e.ManagerId, cancellationToken);

            var visited = new HashSet<Guid>();
            var cursor = request.ManagerId;
            while (cursor is not null)
            {
                if (cursor == request.Id)
                    return Fail(Error.Conflict("This assignment would create a circular management hierarchy."));
                if (!visited.Add(cursor.Value))
                    break;
                cursor = allEmployees.TryGetValue(cursor.Value, out var next) ? next : null;
            }
        }

        if (request.EmploymentTypeId.HasValue)
        {
            var etExists = await _dbContext.EmploymentTypes.AnyAsync(
                t => t.Id == request.EmploymentTypeId && t.CompanyId == request.CompanyId && t.IsActive, cancellationToken);
            if (!etExists)
                return Fail(Error.NotFound($"Employment type '{request.EmploymentTypeId}' was not found or is inactive."));
        }

        // ---- Employment: status transition guards --------------------------------------------
        if (request.Status == EmploymentStatus.Draft && employee.Status != EmploymentStatus.Draft)
            return Fail(Error.Validation("Cannot set employment status to Draft."));

        if (request.Status == EmploymentStatus.FormerEmployee && employee.Status != request.Status)
            return Fail(Error.Validation("Cannot set employment status to Former Employee directly."));

        if (request.Status == EmploymentStatus.Leaving &&
            employee.Status != EmploymentStatus.Leaving &&
            employee.LeavingDate is null)
            return Fail(Error.Validation("Cannot set employment status to Leaving without a leaving date. Use the Start Leaving Process action instead."));

        // ---- Apply mutations -----------------------------------------------------------------
        var now = _clock.UtcNowOffset();

        var profileBefore = SnapshotProfile(employee);
        var employmentBefore = SnapshotEmployment(employee);
        var overallPositionBefore = employee.PositionProfileId;
        var overallLocationBefore = employee.LocationId;
        var overallManagerBefore = employee.ManagerId;

        // Profile mutations first; the employment block has the final say over the shared fields.
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

        employee.SetSystemAccess(request.HasSystemAccess, now);

        // Employment status transitions.
        if (employee.Status != request.Status)
        {
            switch (request.Status)
            {
                case EmploymentStatus.Active:    employee.Activate(now);   break;
                case EmploymentStatus.Suspended: employee.Suspend(now);    break;
                case EmploymentStatus.Leaving:   employee.SetLeaving(now); break;
            }
        }

        employee.UpdateEmploymentDetails(
            request.EmployeeNumber ?? employee.EmployeeNumber,
            request.EmploymentTypeId ?? employee.EmploymentTypeId,
            request.StartDate,
            request.ContinuousServiceDate,
            request.ProbationEndDate,
            request.LeavingDate,
            request.Notes,
            now,
            request.NoticePeriodUnitOverride,
            request.NoticePeriodLengthOverride);

        employee.Assign(
            request.DepartmentId ?? employee.DepartmentId,
            request.PositionProfileId ?? employee.PositionProfileId,
            request.LocationId ?? employee.LocationId,
            request.ManagerId,
            now);

        employee.SetWorkingPattern(request.WorkingDaysOverride, request.HoursPerDayOverride, now);

        // Seed-admin completion: the one automatic Draft -> Active transition in the system.
        if (employee.IsInitialCompanyAdmin && employee.Status == EmploymentStatus.Draft)
            employee.Activate(now);

        // ---- Single guarded commit ----------------------------------------------------------
        var saveResult = await _dbContext.SaveChangesWithConcurrencyAsync(
            employee,
            request.ExpectedVersion,
            "This employee's details were changed by someone else since you opened them. Reload the latest details and try again.",
            cancellationToken);

        if (saveResult.IsFailure)
            return Fail(saveResult.Error);

        // ---- Post-commit: merged audit + integration events --------------------------------
        var correlationId = request.CorrelationId ?? Guid.NewGuid();
        var profileAfter = SnapshotProfile(employee);
        var employmentAfter = SnapshotEmployment(employee);

        await _auditEventPublisher.PublishAsync(
            new EmployeeProfileUpdatedAuditEvent(
                employee.CompanyId, employee.Id, actorEmployeeId, now, profileBefore, profileAfter, correlationId),
            cancellationToken);

        await _auditEventPublisher.PublishAsync(
            new EmploymentDetailsUpdatedAuditEvent(
                employee.CompanyId, employee.Id, actorEmployeeId, now, employmentBefore, employmentAfter, correlationId),
            cancellationToken);

        if (overallPositionBefore != employee.PositionProfileId)
        {
            await _integrationEventPublisher.PublishAsync(
                new EmployeePositionChangedIntegrationEvent(
                    employee.CompanyId, employee.Id, overallPositionBefore, employee.PositionProfileId, now),
                cancellationToken);
        }

        if (overallLocationBefore != employee.LocationId)
        {
            await _integrationEventPublisher.PublishAsync(
                new EmployeeLocationChangedIntegrationEvent(
                    employee.CompanyId, employee.Id, overallLocationBefore, employee.LocationId, now),
                cancellationToken);
        }

        if (overallManagerBefore != employee.ManagerId)
        {
            await _integrationEventPublisher.PublishAsync(
                new EmployeeManagerChangedIntegrationEvent(
                    employee.CompanyId, employee.Id, overallManagerBefore, employee.ManagerId, now),
                cancellationToken);
        }

        var otherProfileFieldsChanged =
            profileBefore.FirstName != profileAfter.FirstName ||
            profileBefore.LastName != profileAfter.LastName ||
            profileBefore.WorkEmail != profileAfter.WorkEmail ||
            profileBefore.PersonalEmail != profileAfter.PersonalEmail ||
            profileBefore.StartDate != profileAfter.StartDate ||
            profileBefore.PreferredName != profileAfter.PreferredName ||
            profileBefore.DateOfBirth != profileAfter.DateOfBirth ||
            profileBefore.Nationality != profileAfter.Nationality ||
            profileBefore.Gender != profileAfter.Gender ||
            profileBefore.GenderOther != profileAfter.GenderOther ||
            profileBefore.DepartmentId != profileAfter.DepartmentId ||
            profileBefore.HasSystemAccess != profileAfter.HasSystemAccess;

        if (otherProfileFieldsChanged)
        {
            await _integrationEventPublisher.PublishAsync(
                new EmployeeDetailsCorrectedIntegrationEvent(employee.CompanyId, employee.Id, now),
                cancellationToken);
        }

        return Result.Success(new UpdateEmployeeProfileAndEmploymentResponse(
            employee.Id,
            employee.CompanyId,
            employee.FirstName,
            employee.LastName,
            employee.WorkEmail,
            employee.PersonalEmail,
            employee.EmployeeNumber,
            employee.EmploymentTypeId,
            employee.Status,
            employee.DepartmentId,
            employee.LocationId,
            employee.PositionProfileId,
            employee.ManagerId,
            employee.StartDate,
            employee.ContinuousServiceDate,
            employee.ProbationEndDate,
            employee.LeavingDate,
            employee.NoticePeriodUnitOverride,
            employee.NoticePeriodLengthOverride,
            employee.WorkingDaysOverride,
            employee.HoursPerDayOverride,
            employee.Notes,
            employee.HasSystemAccess,
            employee.UpdatedAt,
            employee.Version));
    }

    private static EmployeeProfileSnapshot SnapshotProfile(Domain.Employee e) => new(
        e.FirstName, e.LastName, e.WorkEmail, e.PersonalEmail, e.StartDate, e.PreferredName,
        e.DateOfBirth, e.Nationality, e.Gender, e.GenderOther, e.DepartmentId, e.PositionProfileId,
        e.LocationId, e.HasSystemAccess);

    private static EmploymentDetailsSnapshot SnapshotEmployment(Domain.Employee e) => new(
        e.EmployeeNumber, e.EmploymentTypeId, e.StartDate, e.ContinuousServiceDate, e.ProbationEndDate,
        e.LeavingDate, e.Notes, e.ManagerId);
}
