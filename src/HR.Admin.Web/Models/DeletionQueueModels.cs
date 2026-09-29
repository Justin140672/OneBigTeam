namespace HR.Admin.Web.Models;

public sealed record DeletionQueueResponse(IReadOnlyList<DeletionQueueItem> Items);

public sealed record DeletionQueueItem(
    Guid CompanyId,
    string CompanyName,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? ExecutedAt);

public sealed record ScheduleDeletionRequest(Guid CompanyId, string Reason, int? CountdownDays);

public sealed record ScheduleDeletionResponse(Guid CompanyId, DateTimeOffset DeletionScheduledAt);

public sealed record CancelDeletionRequest(Guid CompanyId, string Reason);

public sealed record CancelDeletionResponse(Guid CompanyId, DateTimeOffset DeletionCancelledAt);

public sealed record ExecuteDeletionRequest(Guid CompanyId, string Reason);

public sealed record ExecuteDeletionResponse(Guid CompanyId, DateTimeOffset DeletionExecutedAt);
