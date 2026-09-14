using HR.SharedKernel.Idempotency;

namespace HR.Modules.Assets.Persistence;

/// <summary>
/// This module's own idempotency-record entity (ticket 3, P1 follow-up). Kept internal to this
/// assembly - see <see cref="IIdempotencyRecord"/> for why a shared public entity type isn't used.
/// </summary>
internal sealed class IdempotencyRecord : IIdempotencyRecord
{
    public string OperationId { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public Guid ActorId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public int ResponseStatusCode { get; set; }
    public string ResponseBodyJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
