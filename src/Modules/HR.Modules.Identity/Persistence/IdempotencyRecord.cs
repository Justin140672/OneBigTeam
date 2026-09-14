using HR.SharedKernel.Idempotency;

namespace HR.Modules.Identity.Persistence;

/// <summary>
/// Module-local idempotency-record entity (ticket 3, P1 follow-up). Each module declares its own
/// small internal class implementing <see cref="IIdempotencyRecord"/> - a shared public entity type
/// would trip every module's "entity types must not be public" architecture test - so the shared
/// save/replay algorithm in <see cref="DbContextIdempotencyExtensions"/> is generic over this type.
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
