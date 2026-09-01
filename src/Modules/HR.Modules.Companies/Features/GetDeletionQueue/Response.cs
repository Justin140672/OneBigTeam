namespace HR.Modules.Companies.Features.GetDeletionQueue;

internal sealed record GetDeletionQueueResponse(IReadOnlyList<DeletionQueueItemDto> Items);

/// <summary>
/// One row per company that currently has, or has ever had, a deletion scheduled. Countdown is
/// deliberately not included as a server-computed field — the Admin Portal UI computes a live
/// countdown from ScheduledAt against the current time, so it keeps ticking between page loads
/// without needing to re-poll the API.
/// </summary>
internal sealed record DeletionQueueItemDto(
    Guid CompanyId,
    string CompanyName,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? ExecutedAt,
    DateTimeOffset? LegalHoldPlacedAt,
    string? LegalHoldReason);
