using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetMyEmergencyContacts;

internal sealed class GetMyEmergencyContactsHandler(EmployeesDbContext dbContext)
{
    public async Task<Result<GetMyEmergencyContactsResponse>> HandleAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employeeExists = await dbContext.Employees
            .AnyAsync(e => e.CompanyId == companyId && e.Id == employeeId, cancellationToken);

        if (!employeeExists)
            return Result.Failure<GetMyEmergencyContactsResponse>(
                Error.NotFound("No employee record is linked to this user."));

        var contacts = await dbContext.EmergencyContacts
            .Where(c => c.CompanyId == companyId && c.EmployeeId == employeeId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new EmergencyContactItem(c.Id, c.Name, c.Relationship, c.PhoneNumber, c.Email))
            .ToListAsync(cancellationToken);

        return Result.Success(new GetMyEmergencyContactsResponse(contacts));
    }
}
