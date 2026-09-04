using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskCreator(
    TasksDbContext dbContext,
    INotificationWriter notificationWriter,
    IClock clock,
    IAuditEventPublisher auditPublisher) : ITaskCreator
{
    private const string IdempotencyIndexName = "ix_task_items_company_id_idempotency_key";

    public async Task<Guid> CreateAsync(
        Guid companyId,
        Guid createdBy,
        string title,
        string? description,
        TaskPriority priority,
        TaskSource source,
        TaskActionType actionType,
        DateOnly? dueDate,
        Guid? assignedEmployeeId,
        Guid? assignedUserId,
        Guid? sourceEntityId,
        CancellationToken cancellationToken,
        bool notifyAssignee = true,
        string? idempotencyKey = null)
    {
        // OBT-REM-13: read-before-create optimisation only — cheaply avoids the round trip to build
        // and attempt-insert a task (plus its notification/audit side effects) in the common case
        // where a prior call already won. This check is not itself concurrency-safe (classic
        // check-then-act), so correctness against concurrent/retried callers comes from the unique
        // database constraint handled below, not from this check.
        if (idempotencyKey is not null)
        {
            var existingId = await dbContext.TaskItems
                .Where(t => t.CompanyId == companyId && t.IdempotencyKey == idempotencyKey)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingId != Guid.Empty)
                return existingId;
        }

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, createdBy,
            title.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            priority, source, actionType, dueDate, assignedEmployeeId, assignedUserId,
            clock.UtcNowOffset(), sourceEntityId, idempotencyKey);

        dbContext.TaskItems.Add(task);

        if (idempotencyKey is not null)
        {
            var created = await TrySaveIdempotentlyAsync(task, cancellationToken);
            if (!created)
            {
                // A concurrent or retried caller already won the (company_id, idempotency_key)
                // race — treat this as a successful idempotent replay: no duplicate task, no
                // duplicate notification, no duplicate audit event. Return the winner's Id so
                // callers see a consistent result either way.
                var winnerId = await dbContext.TaskItems
                    .AsNoTracking()
                    .Where(t => t.CompanyId == companyId && t.IdempotencyKey == idempotencyKey)
                    .Select(t => t.Id)
                    .FirstAsync(cancellationToken);
                return winnerId;
            }
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (assignedEmployeeId.HasValue && notifyAssignee)
        {
            // NOT-03: TaskAssigned is one of the six template-backed notification types (see
            // NotificationTemplateCatalogue). The rendered in-app title/body reproduce exactly what
            // the previous inline "$New task assigned: {task.Title}$" / task.Description strings
            // produced.
            var tokens = new Dictionary<string, string> { ["TaskTitle"] = task.Title };
            if (!string.IsNullOrWhiteSpace(task.Description))
                tokens["TaskDescription"] = task.Description;

            var writeResult = await notificationWriter.WriteTemplatedAsync(
                Guid.NewGuid(), companyId, assignedEmployeeId.Value,
                NotificationType.TaskAssigned,
                tokens,
                task.Id,
                ToNotificationPriority(priority),
                clock.UtcNowOffset(),
                cancellationToken);

            // TaskTitle is always present (see above), so this should never actually fail — but
            // surface it loudly rather than silently swallowing a template regression.
            if (writeResult.IsFailure)
                throw new InvalidOperationException($"Failed to write TaskAssigned notification: {writeResult.Error.Message}");
        }

        await auditPublisher.PublishAsync(new TaskCreatedAuditEvent(
            task.CompanyId,
            task.Id,
            task.CreatedBy,
            task.Title,
            task.Priority.ToString(),
            task.Source.ToString(),
            task.AssignedEmployeeId,
            task.AssignedUserId,
            task.CreatedAt), cancellationToken);

        return task.Id;
    }

    /// <summary>
    /// Persists the pending <see cref="TaskItem"/> added to the change tracker by the caller.
    /// Returns <c>true</c> when this call inserted the row, <c>false</c> when a concurrent or
    /// retried caller had already inserted a task for the same (company_id, idempotency_key) pair
    /// (PostgreSQL 23505 on <see cref="IdempotencyIndexName"/>). Any other database error — a
    /// different constraint violation, a transient failure, etc. — propagates unchanged; only the
    /// specific idempotency-index violation is swallowed here.
    /// </summary>
    private async Task<bool> TrySaveIdempotentlyAsync(TaskItem task, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (PostgresUniqueViolation.Is(exception, IdempotencyIndexName))
        {
            // Detach the entity this losing caller tried (and failed) to insert so the shared
            // scoped DbContext remains safe to reuse for the lookup below and any later use.
            var entry = dbContext.Entry(task);
            if (entry.State != EntityState.Detached)
                entry.State = EntityState.Detached;

            return false;
        }
    }

    private static NotificationPriority ToNotificationPriority(TaskPriority priority) => priority switch
    {
        TaskPriority.Critical => NotificationPriority.Urgent,
        TaskPriority.High     => NotificationPriority.High,
        TaskPriority.Medium   => NotificationPriority.Normal,
        _                     => NotificationPriority.Low,
    };
}
