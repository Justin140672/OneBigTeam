using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetEmployeeEmergencyContacts;

internal sealed class GetEmployeeEmergencyContactsHandler(EmployeesDbContext dbContext)
{
    public async Task<Result<GetEmployeeEmergencyContactsResponse>> HandleAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employeeExists = await dbContext.Employees
            .AnyAsync(e => e.CompanyId == companyId && e.Id == employeeId, cancellationToken);

        if (!employeeExists)
            return Result.Failure<GetEmployeeEmergencyContactsResponse>(
                Error.NotFound($"Employee '{employeeId}' was not found."));

        var contacts = await dbContext.EmergencyContacts
            .Where(c => c.CompanyId == companyId && c.EmployeeId == employeeId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new EmployeeEmergencyContactItem(c.Id, c.Name, c.Relationship, c.PhoneNumber, c.Email))
            .ToListAsync(cancellationToken);

        return Result.Success(new GetEmployeeEmergencyContactsResponse(contacts));
    }
}
