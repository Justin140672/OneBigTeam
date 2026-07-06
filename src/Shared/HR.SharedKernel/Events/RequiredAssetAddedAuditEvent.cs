namespace HR.SharedKernel;

public sealed record RequiredAssetAddedAuditEvent(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid RequiredAssetId,
    Guid AssetCategoryId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "position-profile.required-asset.added";
    string IAuditEvent.EntityType => "PositionProfile";
    Guid IAuditEvent.EntityId => PositionProfileId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Required asset added to position profile";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { RequiredAssetId, AssetCategoryId };
    object? IAuditEvent.Metadata => null;
}
