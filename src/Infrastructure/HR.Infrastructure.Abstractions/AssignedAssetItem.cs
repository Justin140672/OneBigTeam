namespace HR.Infrastructure.Abstractions;

public sealed record AssignedAssetItem(
    Guid AssetAssignmentId,
    Guid AssetId,
    string AssetLabel);
