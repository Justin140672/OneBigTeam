using System.Security.Cryptography;
using System.Text;

namespace HR.SharedKernel.ExecutionContext;

/// <summary>
/// Ticket 23 follow-up: maps the ambient execution context's string
/// <see cref="IExecutionContext.CorrelationId"/> to a stable <see cref="Guid"/> for the Guid-typed
/// persistence columns/fields that pre-date (and are simplest to keep as) UUIDs — e.g.
/// <c>IAuditEvent.CorrelationId</c> and the various outbox/operation tables' <c>correlation_id</c>
/// columns (see specifications/architecture/05-database-standards.md "UUID Primary Keys" — the same
/// convention extends naturally to correlation columns).
///
/// Rule: if the supplied string is already a well-formed GUID (any format <see cref="Guid.TryParse"/>
/// accepts), that exact Guid is returned unchanged — this keeps the common case (a caller supplying
/// a real GUID, or this codebase's own generated root correlation ids) round-tripping to the exact
/// same value end to end. Otherwise a stable Guid is deterministically derived from the string's
/// bytes (SHA-256, first 16 bytes) — NOT for cryptographic purposes, only so the same opaque
/// correlation string always maps to the same Guid (needed for grouping/lookup in a Guid column),
/// without ever colliding differently for the same input.
/// </summary>
public static class CorrelationIdGuid
{
    public static Guid Derive(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        if (Guid.TryParse(correlationId, out var guid))
            return guid;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(correlationId));
        return new Guid(hash.AsSpan(0, 16));
    }
}
