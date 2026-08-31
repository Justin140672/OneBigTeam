using System.Text.RegularExpressions;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Contracts;
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
    private readonly ICompanyEmployeeNumberSettingsReader _employeeNumberSettingsReader;
    private readonly IEmployeeNumberGenerator _employeeNumberGenerator;

    public CreateEmployeeHandler(
        EmployeesDbContext dbContext,
        IClock clock,
        IIntegrationEventPublisher publisher,
        IProbationDateResolver probationDateResolver,
        ICompanyContactValidationReader contactValidationReader,
        ICompanyEmployeeNumberSettingsReader employeeNumberSettingsReader,
        IEmployeeNumberGenerator employeeNumberGenerator)
    {
        _dbContext = dbContext;
        _clock = clock;
        _publisher = publisher;
        _probationDateResolver = probationDateResolver;
        _contactValidationReader = contactValidationReader;
        _employeeNumberSettingsReader = employeeNumberSettingsReader;
        _employeeNumberGenerator = employeeNumberGenerator;
    }

    public async Task<Result<CreateEmployeeResponse>> HandleAsync(
        CreateEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        // NFR-08: idempotency short-circuit. Automated provisioning flows (candidate hire) supply a
        // stable SourceReference. If the upstream workflow is retried after a partial failure, an
        // employee for this source may already exist — return it rather than creating a duplicate,
        // and do NOT re-publish EmployeeCreated (downstream consumers already ran for the first one).
        if (!string.IsNullOrWhiteSpace(request.SourceReference))
        {
            var sourceReference = request.SourceReference.Trim();
            var existingForSource = await _dbContext.Employees
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    e => e.CompanyId == request.CompanyId && e.SourceReference == sourceReference,
                    cancellationToken);

            if (existingForSource is not null)
                return Result.Success(MapResponse(existingForSource));
        }

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

        var employeeNumberMode = await _employeeNumberSettingsReader.GetModeAsync(request.CompanyId, cancellationToken);

        // Normalized the same way Employee.Create/UpdateEmploymentDetails normalize it, so these
        // existence checks compare like-for-like with what the unique index enforces at the DB
        // level.
        string employeeNumber;
        string normalizedEmployeeNumber;

        if (string.IsNullOrWhiteSpace(request.EmployeeNumber))
        {
            if (employeeNumberMode == EmployeeNumberMode.Manual)
            {
                return Result.Failure<CreateEmployeeResponse>(
                    Error.Validation("Employee number is required."));
            }

            // Automatic mode: caller didn't supply one, generate it via the atomic counter and
            // retry on conflict. The counter itself is race-free (a single UPDATE ... RETURNING
            // relying on Postgres's row lock — see EmployeeNumberGenerator's own remarks), so two
            // concurrent callers can never claim the same number from each other. But the stored
            // "next" value can still drift out of sync with actual data by means outside this
            // handler's control entirely — e.g. an admin directly editing "Next Number" on HR
            // Settings to a value at or behind one already claimed. Retry with a fresh claim
            // instead of failing the whole request outright; bounded, since a conflict persisting
            // past a handful of attempts indicates something more seriously wrong than ordinary
            // drift.
            const int maxAttempts = 5;
            var attempt = 0;
            while (true)
            {
                attempt++;
                employeeNumber = await _employeeNumberGenerator.GenerateNextAsync(request.CompanyId, cancellationToken);
                normalizedEmployeeNumber = employeeNumber.ToUpperInvariant();

                var candidateExists = await _dbContext.Employees
                    .AnyAsync(
                        e => e.CompanyId == request.CompanyId &&
                             e.EmployeeNumber == normalizedEmployeeNumber,
                        cancellationToken);

                if (!candidateExists)
                    break;

                if (attempt >= maxAttempts)
                {
                    return Result.Failure<CreateEmployeeResponse>(
                        Error.Conflict("Could not generate a unique employee number after several attempts."));
                }
            }
        }
        else
        {
            employeeNumber = request.EmployeeNumber.Trim();
            normalizedEmployeeNumber = employeeNumber.ToUpperInvariant();

            var employeeNumberExists = await _dbContext.Employees
                .AnyAsync(
                    e => e.CompanyId == request.CompanyId &&
                         e.EmployeeNumber == normalizedEmployeeNumber,
                    cancellationToken);

            if (employeeNumberExists)
            {
                return Result.Failure<CreateEmployeeResponse>(
                    Error.Conflict($"An employee with employee number '{employeeNumber}' already exists in this company."));
            }
        }

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

        var locationExists = await _dbContext.Locations
            .AnyAsync(
                l => l.Id == request.LocationId &&
                     l.CompanyId == request.CompanyId &&
                     l.IsActive,
                cancellationToken);

        if (!locationExists)
        {
            return Result.Failure<CreateEmployeeResponse>(
                Error.NotFound($"Location '{request.LocationId}' was not found."));
        }

        var positionProfile = await _dbContext.PositionProfiles
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

        var employmentTypeExists = await _dbContext.EmploymentTypes
            .AnyAsync(
                t => t.Id == request.EmploymentTypeId &&
                     t.CompanyId == request.CompanyId &&
                     t.IsActive,
                cancellationToken);

        if (!employmentTypeExists)
        {
            return Result.Failure<CreateEmployeeResponse>(
                Error.NotFound($"Employment type '{request.EmploymentTypeId}' was not found."));
        }

        if (request.ManagerId is not null)
        {
            var managerExists = await _dbContext.Employees
                .AnyAsync(
                    e => e.Id == request.ManagerId &&
                         e.CompanyId == request.CompanyId &&
                         e.Status != EmploymentStatus.FormerEmployee,
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
            request.DateOfBirth,
            request.Nationality.Trim(),
            request.Gender.Trim(),
            employeeNumber,
            request.EmploymentTypeId,
            request.DepartmentId,
            request.LocationId,
            request.PositionProfileId,
            now,
            request.SourceReference);

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

        employee.Assign(request.DepartmentId, request.PositionProfileId, request.LocationId, request.ManagerId, now);

        var probationEndDate = await _probationDateResolver.ResolveEndDateAsync(
            request.CompanyId, positionProfile?.ProbationMonthsOverride, employee.StartDate, cancellationToken);
        employee.SetProbationEndDate(probationEndDate, now);

        _dbContext.Employees.Add(employee);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // NFR-08: backstop for a concurrent retry of the same automated provisioning workflow —
            // the (CompanyId, SourceReference) filtered unique index rejected this insert because a
            // parallel run already created the employee for this source. Return that row as an
            // idempotent success (no event re-publish) rather than a spurious conflict.
            if (!string.IsNullOrWhiteSpace(request.SourceReference))
            {
                var sourceReference = request.SourceReference.Trim();
                _dbContext.ChangeTracker.Clear();
                var raced = await _dbContext.Employees
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        e => e.CompanyId == request.CompanyId && e.SourceReference == sourceReference,
                        cancellationToken);
                if (raced is not null)
                    return Result.Success(MapResponse(raced));
            }

            // Backstop for the race between the AnyAsync pre-check above and this SaveChangesAsync:
            // the (CompanyId, EmployeeNumber) unique index rejects the duplicate at the database
            // level, and we surface that as the same Conflict error the pre-check would have
            // returned, rather than propagating a raw DB exception.
            return Result.Failure<CreateEmployeeResponse>(
                Error.Conflict($"An employee with employee number '{employeeNumber}' already exists in this company."));
        }

        await _publisher.PublishAsync(new EmployeeCreatedIntegrationEvent(
            employee.CompanyId, employee.Id, employee.StartDate, employee.ManagerId, probationEndDate,
            employee.PositionProfileId, positionProfile?.DefaultLeavePolicyId), cancellationToken);

        return Result.Success(MapResponse(employee));
    }

    private static CreateEmployeeResponse MapResponse(Employee employee) =>
        new(
            employee.Id,
            employee.CompanyId,
            employee.DepartmentId,
            employee.LocationId,
            employee.PositionProfileId,
            employee.ManagerId,
            employee.FirstName,
            employee.LastName,
            employee.WorkEmail,
            employee.PersonalEmail,
            employee.StartDate,
            employee.Status,
            employee.HasSystemAccess,
            employee.CreatedAt);
}
