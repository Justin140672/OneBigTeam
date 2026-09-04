using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateCompensationRecord;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

/// <summary>
/// Implements <see cref="IEmployeeImportWriter"/> for the DataImport module's confirm step.
/// Mirrors CreateEmployeeHandler/SetEmployeeWorkingPatternHandler/CreateCompensationRecordHandler/
/// AssignManagerHandler, but is invoked once per staged import row and does not itself publish
/// EmployeeCreatedIntegrationEvent — that happens once per row from the DataImport handler after
/// all per-row writer calls for that row have succeeded.
/// </summary>
internal sealed class EmployeeImportWriter(
    EmployeesDbContext dbContext,
    IClock clock,
    IProbationDateResolver probationDateResolver,
    IAuditEventPublisher auditEventPublisher,
    IEmployeeNumberGenerator employeeNumberGenerator,
    WorkingPatternCompensationCalculator workingPatternCalculator) : IEmployeeImportWriter
{
    public async Task<EmployeeImportCreateResult> CreateEmployeeAsync(
        EmployeeImportCreateRequest request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        // Automatic-mode rows are staged with no EmployeeNumber (enforced by
        // EmployeeStagingRowValidator) — generate the real number here, at write time, via the
        // same atomic counter CreateEmployeeHandler uses. Each row is written independently and
        // GenerateNextAsync's own UPDATE...RETURNING is atomic per call, so sequential per-row
        // calls are sufficient to guarantee no two rows in the same (or a concurrent) import ever
        // receive the same number — no bulk-reservation step is needed.
        var employeeNumber = string.IsNullOrWhiteSpace(request.EmployeeNumber)
            ? await employeeNumberGenerator.GenerateNextAsync(request.CompanyId, cancellationToken)
            : request.EmployeeNumber.Trim();

        var employee = Employee.Create(
            request.Id,
            request.CompanyId,
            request.FirstName,
            request.LastName,
            request.WorkEmail,
            request.StartDate,
            hasSystemAccess: false,
            request.DateOfBirth,
            request.Nationality,
            request.Gender,
            employeeNumber,
            request.EmploymentTypeId,
            request.DepartmentId,
            request.LocationId,
            request.PositionProfileId,
            now);

        employee.UpdatePersonalDetails(
            string.IsNullOrWhiteSpace(request.PreferredName) ? request.FirstName : request.PreferredName,
            request.DateOfBirth,
            request.Nationality,
            request.Gender,
            genderOther: null,
            now);

        employee.UpdateContactDetails(
            request.PersonalEmail, null, null, request.Address, null, null, null, null, null, now);

        var positionProfile = await dbContext.PositionProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.Id == request.PositionProfileId && p.CompanyId == request.CompanyId,
                cancellationToken);

        // Use the imported Probation End Date when the file specified one; otherwise fall back to
        // the company's default calculation, exactly as before this field was captured.
        var probationEndDate = request.ProbationEndDate ?? await probationDateResolver.ResolveEndDateAsync(
            request.CompanyId, positionProfile?.ProbationMonthsOverride, employee.StartDate, cancellationToken);
        employee.SetProbationEndDate(probationEndDate, now);

        // Employee.Create leaves Status = Draft. Previously this unconditionally activated every
        // imported employee regardless of start date — wrong for a future starter, and (by
        // accident) right for one who has already started. Only activate here when the start date
        // has already arrived; a future start date correctly stays Draft until that date arrives.
        // Note: there is currently no scheduled job that transitions Draft -> Active once a future
        // start date arrives for ANY employee (manually created or imported) — this only fixes
        // import so it never leaves an already-started employee incorrectly stuck in Draft.
        if (request.StartDate <= DateOnly.FromDateTime(now.Date))
            employee.Activate(now);

        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new EmployeeCreatedAuditEvent(
                request.CompanyId, employee.Id, request.ActorUserId, now, "Import", request.ImportSessionId),
            cancellationToken);

        return new EmployeeImportCreateResult(
            employee.Id,
            employee.EmployeeNumber,
            employee.StartDate,
            employee.ManagerId,
            employee.PositionProfileId,
            probationEndDate,
            positionProfile?.DefaultLeavePolicyId);
    }

    public async Task<EmployeeImportCreateResult> UpdateEmployeeAsync(
        Guid existingEmployeeId, EmployeeImportCreateRequest request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var employee = await dbContext.Employees
            .SingleAsync(e => e.Id == existingEmployeeId && e.CompanyId == request.CompanyId, cancellationToken);

        employee.UpdateProfile(
            request.FirstName, request.LastName, request.WorkEmail, request.PersonalEmail, request.StartDate, now);

        employee.UpdatePersonalDetails(
            string.IsNullOrWhiteSpace(request.PreferredName) ? request.FirstName : request.PreferredName,
            request.DateOfBirth,
            request.Nationality,
            request.Gender,
            genderOther: null,
            now);

        employee.Assign(request.DepartmentId, request.PositionProfileId, request.LocationId, employee.ManagerId, now);

        if (!string.IsNullOrWhiteSpace(request.Address))
        {
            employee.UpdateContactDetails(
                employee.PersonalEmail, employee.PhoneNumber, employee.HomePhone, request.Address,
                employee.AddressLine2, employee.City, employee.County, employee.PostCode, employee.Country, now);
        }

        var positionProfile = await dbContext.PositionProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.Id == request.PositionProfileId && p.CompanyId == request.CompanyId,
                cancellationToken);

        var probationEndDate = request.ProbationEndDate ?? await probationDateResolver.ResolveEndDateAsync(
            request.CompanyId, positionProfile?.ProbationMonthsOverride, employee.StartDate, cancellationToken);
        employee.SetProbationEndDate(probationEndDate, now);

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new EmployeeCreatedAuditEvent(
                request.CompanyId, employee.Id, request.ActorUserId, now, "Import", request.ImportSessionId),
            cancellationToken);

        return new EmployeeImportCreateResult(
            employee.Id,
            employee.EmployeeNumber,
            employee.StartDate,
            employee.ManagerId,
            employee.PositionProfileId,
            probationEndDate,
            positionProfile?.DefaultLeavePolicyId);
    }

    public async Task SetWorkingPatternAsync(
        Guid companyId, Guid employeeId, EmployeeImportWorkingPattern pattern, CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .SingleOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, cancellationToken);

        if (employee is null)
            return;

        employee.SetWorkingPattern(pattern.WorkingDays, pattern.HoursPerDay, clock.UtcNowOffset());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CreateOpeningCompensationAsync(
        Guid companyId, Guid employeeId, DateOnly effectiveFrom, EmployeeImportCompensation compensation,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<SalaryType>(compensation.SalaryType, ignoreCase: true, out var salaryType))
            return;

        var now = clock.UtcNowOffset();

        var employee = await dbContext.Employees
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, cancellationToken);

        var (hoursPerWeek, fte) = await workingPatternCalculator.CalculateAsync(
            companyId, employee?.WorkingDaysOverride, employee?.HoursPerDayOverride, cancellationToken);

        var record = Compensation.Create(
            Guid.NewGuid(),
            companyId,
            employeeId,
            effectiveFrom,
            salaryType,
            compensation.SalaryAmount,
            compensation.Currency.Trim().ToUpperInvariant(),
            hoursPerWeek,
            fte,
            notes: "Imported",
            CompensationChangeReason.DataImported,
            employeeId,
            now);

        dbContext.Compensations.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryAssignManagerAsync(
        Guid companyId, Guid employeeId, Guid managerId, CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .SingleOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, cancellationToken);

        if (employee is null)
            return false;

        var manager = await dbContext.Employees
            .SingleOrDefaultAsync(
                e => e.Id == managerId && e.CompanyId == companyId && e.Status != EmploymentStatus.FormerEmployee,
                cancellationToken);

        if (manager is null)
            return false;

        // Circular hierarchy check, replicated from AssignManagerHandler: walk up the proposed
        // manager's chain and bail out if we would reach the employee being updated.
        var allEmployees = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId)
            .Select(e => new { e.Id, e.ManagerId })
            .ToDictionaryAsync(e => e.Id, e => e.ManagerId, cancellationToken);

        var visited = new HashSet<Guid>();
        var cursor = (Guid?)managerId;

        while (cursor is not null)
        {
            if (cursor == employeeId)
                return false;

            if (!visited.Add(cursor.Value))
                break;

            cursor = allEmployees.TryGetValue(cursor.Value, out var nextManagerId) ? nextManagerId : null;
        }

        employee.Assign(employee.DepartmentId, employee.PositionProfileId, employee.LocationId, managerId, clock.UtcNowOffset());
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<EmployeeImportCreateResult?> GetImportSnapshotAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, cancellationToken);

        if (employee is null)
            return null;

        var positionProfile = await dbContext.PositionProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.Id == employee.PositionProfileId && p.CompanyId == companyId,
                cancellationToken);

        return new EmployeeImportCreateResult(
            employee.Id,
            employee.EmployeeNumber,
            employee.StartDate,
            employee.ManagerId,
            employee.PositionProfileId,
            employee.ProbationEndDate ?? employee.StartDate,
            positionProfile?.DefaultLeavePolicyId);
    }
}
