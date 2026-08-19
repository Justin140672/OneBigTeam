using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetEmployeeNotes;

internal sealed class GetEmployeeNotesHandler(
    EmployeesDbContext dbContext,
    IEmployeeNameReader employeeNameReader)
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

        var notes = await dbContext.EmployeeNotes
            .Where(n => n.CompanyId == companyId && n.EmployeeId == employeeId)
            .OrderByDescending(n => n.CreatedDate)
            .ToListAsync(cancellationToken);

        // CreatedByUserId is the acting employee's own id (see CreateEmployeeNoteHandler, which
        // passes actorEmployeeId — despite the "UserId" name, matching the same convention already
        // used by GetEmployeeTimeline's PerformedBy resolution), so it resolves via the same
        // employee-id-keyed name reader rather than a separate identity/user lookup.
        var names = await employeeNameReader.GetNamesAsync(
            companyId, notes.Select(n => n.CreatedByUserId).Distinct(), cancellationToken);

        var items = notes
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
                names.GetValueOrDefault(n.CreatedByUserId, "Unknown"),
                n.CreatedDate))
            .ToList();

        return Result.Success(new GetEmployeeNotesResponse(items));
    }
}
