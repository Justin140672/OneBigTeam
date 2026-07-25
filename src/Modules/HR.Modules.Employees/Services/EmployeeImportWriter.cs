using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateCompensationRecord;
using HR.Modules.Employees.Persistence;
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
    IAuditEventPublisher auditEventPublisher) : IEmployeeImportWriter
{
    public async Task<EmployeeImportCreateResult> CreateEmployeeAsync(
        EmployeeImportCreateRequest request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

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
            request.EmployeeNumber,
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
            request.PersonalEmail, null, null, null, null, null, null, null, null, now);

        var positionProfile = await dbContext.PositionProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.Id == request.PositionProfileId && p.CompanyId == request.CompanyId,
                cancellationToken);

        var probationEndDate = await probationDateResolver.ResolveEndDateAsync(
            request.CompanyId, positionProfile?.ProbationMonthsOverride, employee.StartDate, cancellationToken);
        employee.SetProbationEndDate(probationEndDate, now);

        employee.Activate(now);

        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new EmployeeCreatedAuditEvent(
                request.CompanyId, employee.Id, request.ActorUserId, now, "Import", request.ImportSessionId),
            cancellationToken);

        return new EmployeeImportCreateResult(
            employee.Id,
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

        var record = Compensation.Create(
            Guid.NewGuid(),
            companyId,
            employeeId,
            effectiveFrom,
            salaryType,
            compensation.SalaryAmount,
            compensation.Currency.Trim().ToUpperInvariant(),
            compensation.HoursPerWeek,
            compensation.Fte,
            notes: "Imported",
            CompensationChangeReason.NewHire,
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
}
