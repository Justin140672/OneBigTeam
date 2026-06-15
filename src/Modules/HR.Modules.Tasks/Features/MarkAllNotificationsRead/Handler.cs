using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.MarkAllNotificationsRead;

internal sealed class MarkAllNotificationsReadHandler(TasksDbContext dbContext)
{
    public async Task HandleAsync(MarkAllNotificationsReadRequest request, CancellationToken cancellationToken)
    {
        var unread = await dbContext.Notifications
            .Where(n => n.CompanyId == request.CompanyId
                     && n.EmployeeId == request.EmployeeId
                     && !n.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var n in unread)
            n.MarkAsRead();

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
