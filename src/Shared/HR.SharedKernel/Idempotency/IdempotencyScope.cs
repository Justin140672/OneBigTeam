namespace HR.SharedKernel.Idempotency;

/// <summary>
/// Ticket 3 (P1) follow-up: the security/ownership boundary an idempotency key is scoped to.
/// Two requests only ever replay each other's result when their scope AND key AND fingerprint all
/// match - a key alone is never trusted as sufficient identity.
/// </summary>
/// <param name="OperationId">Stable identifier for the endpoint/operation (e.g. the handler's own name).</param>
/// <param name="CompanyId">Tenant scope, or <see cref="Guid.Empty"/> when not applicable.</param>
/// <param name="ActorId">Authenticated-actor scope, or <see cref="Guid.Empty"/> when not applicable.</param>
public readonly record struct IdempotencyScope(string OperationId, Guid CompanyId, Guid ActorId);
