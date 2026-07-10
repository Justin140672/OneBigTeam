namespace HR.Infrastructure.Abstractions;

public sealed record OutstandingAssetAcknowledgementItem(
    Guid AssetAssignmentId,
    Guid AssetId,
    string AssetLabel,
    DateTimeOffset AssignedAt);
