using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.RemoveMyEmergencyContact;

internal sealed class RemoveMyEmergencyContactHandler(EmployeesDbContext dbContext)
{
    public async Task<Result> HandleAsync(
        Guid companyId,
        Guid employeeId,
        Guid contactId,
        CancellationToken cancellationToken)
    {
        var contact = await dbContext.EmergencyContacts
            .SingleOrDefaultAsync(
                c => c.CompanyId == companyId &&
                     c.EmployeeId == employeeId &&
                     c.Id == contactId,
                cancellationToken);

        if (contact is null)
            return Result.Failure(Error.NotFound("Emergency contact not found."));

        dbContext.EmergencyContacts.Remove(contact);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
