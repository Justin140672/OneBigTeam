using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetEmployeeNotes;

internal sealed class GetEmployeeNotesHandler(EmployeesDbContext dbContext)
{
    public async Task<Result<GetEmployeeNotesResponse>> HandleAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employeeExists = await dbContext.Employees
            .AnyAsync(e => e.CompanyId == companyId && e.Id == employeeId, cancellationToken);

        if (!employeeExists)
            return Result.Failure<GetEmployeeNotesResponse>(
                Error.NotFound($"Employee '{employeeId}' was not found."));

        var items = await dbContext.EmployeeNotes
            .Where(n => n.CompanyId == companyId && n.EmployeeId == employeeId)
            .OrderByDescending(n => n.CreatedDate)
            .Select(n => new EmployeeNoteItem(
                n.Id,
                n.CompanyId,
                n.EmployeeId,
                n.Category.ToString(),
                n.NoteText,
                n.IsImportant,
                n.IsSuperseded,
                n.SupersededByNoteId,
                n.CreatedByUserId,
                n.CreatedDate))
            .ToListAsync(cancellationToken);

        return Result.Success(new GetEmployeeNotesResponse(items));
    }
}
