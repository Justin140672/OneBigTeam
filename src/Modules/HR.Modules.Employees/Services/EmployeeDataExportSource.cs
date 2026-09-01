using System.Globalization;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

/// <summary>
/// Story 2: contributes the Employees module's principal data (employees, departments, emergency
/// contacts) to the organisation data export. company_id is enforced on every query. Deliberately
/// omits free-text <c>Notes</c> and any value a company administrator would not already see in the
/// employee record UI; salary/bank data is owned by Compensation and not surfaced here.
/// </summary>
internal sealed class EmployeeDataExportSource(EmployeesDbContext db) : IEmployeeDataExportSource
{
    public async Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var employees = await db.Employees.AsNoTracking()
            .Where(e => e.CompanyId == companyId)
            .OrderBy(e => e.LastName)
            .Select(e => new
            {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.PreferredName, e.WorkEmail, e.PersonalEmail,
                e.PhoneNumber, e.StartDate, e.LeavingDate, e.Status, e.DepartmentId, e.ManagerId,
                e.DateOfBirth, e.Nationality, e.Gender, e.City, e.County, e.PostCode, e.Country,
                e.ContinuousServiceDate, e.ProbationEndDate, e.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var employeesTable = new DataExportTable(
            "employees",
            ["Id", "EmployeeNumber", "FirstName", "LastName", "PreferredName", "WorkEmail", "PersonalEmail",
             "PhoneNumber", "StartDate", "LeavingDate", "Status", "DepartmentId", "ManagerId", "DateOfBirth",
             "Nationality", "Gender", "City", "County", "PostCode", "Country", "ContinuousServiceDate",
             "ProbationEndDate", "CreatedAt"],
            employees.Select(e => (IReadOnlyList<string?>)new string?[]
            {
                e.Id.ToString(), e.EmployeeNumber, e.FirstName, e.LastName, e.PreferredName, e.WorkEmail, e.PersonalEmail,
                e.PhoneNumber, D(e.StartDate), D(e.LeavingDate), e.Status.ToString(), e.DepartmentId.ToString(),
                e.ManagerId?.ToString(), D(e.DateOfBirth), e.Nationality, e.Gender, e.City, e.County, e.PostCode,
                e.Country, D(e.ContinuousServiceDate), D(e.ProbationEndDate), T(e.CreatedAt)
            }).ToList());

        var departments = await db.Departments.AsNoTracking()
            .Where(d => d.CompanyId == companyId)
            .Select(d => new { d.Id, d.Name, d.Description, d.ParentDepartmentId, d.ManagerEmployeeId, d.IsActive })
            .ToListAsync(cancellationToken);

        var departmentsTable = new DataExportTable(
            "departments",
            ["Id", "Name", "Description", "ParentDepartmentId", "ManagerEmployeeId", "IsActive"],
            departments.Select(d => (IReadOnlyList<string?>)new string?[]
            {
                d.Id.ToString(), d.Name, d.Description, d.ParentDepartmentId?.ToString(),
                d.ManagerEmployeeId?.ToString(), d.IsActive ? "true" : "false"
            }).ToList());

        var contacts = await db.EmergencyContacts.AsNoTracking()
            .Where(c => c.CompanyId == companyId)
            .Select(c => new { c.Id, c.EmployeeId, c.Name, c.Relationship, c.PhoneNumber, c.Email })
            .ToListAsync(cancellationToken);

        var contactsTable = new DataExportTable(
            "emergency_contacts",
            ["Id", "EmployeeId", "Name", "Relationship", "PhoneNumber", "Email"],
            contacts.Select(c => (IReadOnlyList<string?>)new string?[]
            {
                c.Id.ToString(), c.EmployeeId.ToString(), c.Name, c.Relationship, c.PhoneNumber, c.Email
            }).ToList());

        return [employeesTable, departmentsTable, contactsTable];
    }

    private static string? D(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string T(DateTimeOffset value) => value.ToString("o", CultureInfo.InvariantCulture);
}
