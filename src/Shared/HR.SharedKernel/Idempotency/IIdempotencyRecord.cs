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
    /// <summary>Stable identifier for the endpoint/operation (e.g. the handler's own name).</summary>
    string OperationId { get; set; }

    /// <summary>Tenant scope. <see cref="Guid.Empty"/> for operations with no company context.</summary>
    Guid CompanyId { get; set; }

    /// <summary>Authenticated-actor scope. <see cref="Guid.Empty"/> when there is no actor context.</summary>
    Guid ActorId { get; set; }

    /// <summary>The client-supplied Idempotency-Key header value.</summary>
    string Key { get; set; }

    /// <summary>
    /// Hash of the request payload. Guards against a key being reused for a materially different
    /// request within the same scope, which is a caller bug rather than a legitimate retry.
    /// </summary>
    string RequestFingerprint { get; set; }

    int ResponseStatusCode { get; set; }

    string ResponseBodyJson { get; set; }

    DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this record becomes eligible for cleanup. Must exceed the maximum legitimate client
    /// retry window - see <see cref="DbContextIdempotencyExtensions.DefaultRetention"/>.
    /// </summary>
    DateTimeOffset ExpiresAt { get; set; }
}
