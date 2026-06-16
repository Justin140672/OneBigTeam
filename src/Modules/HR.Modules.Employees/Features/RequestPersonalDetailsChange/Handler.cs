using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.RequestPersonalDetailsChange;

internal sealed class RequestPersonalDetailsChangeHandler(
    EmployeesDbContext dbContext,
    ITaskCreator taskCreator)
{
    public async Task<Result<RequestPersonalDetailsChangeResponse>> HandleAsync(
        RequestPersonalDetailsChangeRequest request,
        Guid requestingUserId,
        CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.Id == request.EmployeeId && e.CompanyId == request.CompanyId)
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .SingleOrDefaultAsync(cancellationToken);

        if (employee is null)
            return Result.Failure<RequestPersonalDetailsChangeResponse>(
                Error.NotFound("Employee not found."));

        if (employee.Id != requestingUserId)
            return Result.Failure<RequestPersonalDetailsChangeResponse>(
                Error.Forbidden("You can only request changes for your own profile."));

        var title       = $"Personal Details Change Request: {employee.FirstName} {employee.LastName}";
        var description = request.Notes.Trim();

        var taskId = await taskCreator.CreateAsync(
            companyId:           request.CompanyId,
            createdBy:           requestingUserId,
            title:               title,
            description:         description,
            priority:            TaskPriority.Medium,
            source:              TaskSource.Manual,
            dueDate:             null,
            assignedEmployeeId:  null,
            assignedUserId:      null,
            sourceEntityId:      employee.Id,
            cancellationToken:   cancellationToken);

        return Result.Success(new RequestPersonalDetailsChangeResponse(taskId));
    }
}
