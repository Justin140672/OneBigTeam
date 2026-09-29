namespace HR.SharedKernel.Idempotency;

/// <summary>
/// Contract for a module's own internal idempotency-record entity (each module declares its own
/// small internal class implementing this, the same way <c>PostgresUniqueViolation</c> is
/// duplicated per module) so the shared save/replay algorithm in
/// <see cref="DbContextIdempotencyExtensions"/> and the shared column mapping in
/// <see cref="IdempotencyRecordConfiguration{TRecord}"/> can be written once, while every module's
/// EF entity type stays internal to that module's assembly - a public shared entity type would trip
/// every module's "entity types must not be public" architecture test.
///
/// Ticket 3 (P1) follow-up: the primary key is the composite (<see cref="OperationId"/>,
/// <see cref="CompanyId"/>, <see cref="ActorId"/>, <see cref="Key"/>) rather than the client-supplied
/// key alone. A client-chosen key is not trusted as a security boundary by itself - without the
/// operation/tenant/actor scope, one company or user could supply the same key another caller used
/// and either collide with or replay that caller's stored result. <see cref="RequestFingerprint"/>
/// still detects a key being reused for a materially different payload WITHIN the same scope; it is
/// not itself a substitute for the scope columns.
/// </summary>
public interface IIdempotencyRecord
{
    string OperationId { get; set; }

    Guid CompanyId { get; set; }

    Guid ActorId { get; set; }

    string Key { get; set; }

    string RequestFingerprint { get; set; }

    int ResponseStatusCode { get; set; }

    string ResponseBodyJson { get; set; }

    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset ExpiresAt { get; set; }
}
