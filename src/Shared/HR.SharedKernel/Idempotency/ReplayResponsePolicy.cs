using System.Collections.Concurrent;

namespace HR.SharedKernel.Idempotency;

/// <summary>
/// Ticket 3 (P1) follow-up item 5: guards against persisting a response type that looks like it
/// carries secrets. <see cref="DbContextIdempotencyExtensions.SaveIdempotentAsync{TRecord,TResponse}"/>
/// calls this once per <c>TResponse</c> (a static, per-type cache below means the reflection cost is
/// paid once per type, not per request) before serializing anything - so a handler that accidentally
/// (or in the future) passes a response DTO containing a token/secret/credential-shaped property
/// fails loudly at the point of the save, rather than silently writing it to the idempotency table.
///
/// This is a naming-convention heuristic, not a type allowlist - it deliberately errs toward false
/// positives (rejecting a legitimately-named-but-safe property) over false negatives. A handler that
/// needs to store a narrower field set should introduce a small endpoint-specific replay DTO instead
/// of the full public response, and that DTO won't trip this guard as long as it excludes the
/// sensitive fields by construction.
///
/// Permitted replay-record contents, retention, and erasure handling are documented in
/// docs/security/idempotency-replay-data-policy.md.
/// </summary>
public static class ReplayResponsePolicy
{
    private static readonly string[] ProhibitedNameFragments =
    [
        "token", "secret", "password", "credential", "signature", "apikey", "api_key",
        "refreshtoken", "accesstoken", "connectionstring", "privatekey", "clientsecret",
        "signedurl", "presignedurl", "sastoken", "signeddocument", "signedlink",
    ];

    private const int MaxDepth = 4;

    private static readonly ConcurrentDictionary<Type, string?> Cache = new();

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if <typeparamref name="TResponse"/> (or any
    /// type it exposes a public property of, recursively) has a property whose name matches a
    /// prohibited sensitive-data pattern.
    /// </summary>
    public static void EnsureReplaySafe<TResponse>()
    {
        var violation = Cache.GetOrAdd(typeof(TResponse), static t => FindViolation(t, depth: 0, seen: []));

        if (violation is not null)
        {
            throw new InvalidOperationException(
                $"Refusing to persist '{typeof(TResponse).Name}' for idempotent replay: {violation}. " +
                "Introduce a narrower endpoint-specific replay DTO that excludes this field instead of " +
                "storing the full response. See docs/security/idempotency-replay-data-policy.md.");
        }
    }

    private static string? FindViolation(Type type, int depth, HashSet<Type> seen)
    {
        if (depth > MaxDepth || !seen.Add(type))
            return null;

        // Don't descend into framework/primitive types - nothing user-defined lives there, and it
        // would otherwise recurse into e.g. string's own properties.
        if (type.Namespace is null || type.Namespace.StartsWith("System", StringComparison.Ordinal))
            return null;

        foreach (var property in type.GetProperties())
        {
            var lowerName = property.Name.ToLowerInvariant();
            var match = Array.Find(ProhibitedNameFragments, f => lowerName.Contains(f, StringComparison.Ordinal));
            if (match is not null)
                return $"property '{property.Name}' matches prohibited pattern '{match}'";

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (propertyType.IsEnum || propertyType.IsPrimitive || propertyType == typeof(string) || propertyType == typeof(decimal))
                continue;

            if (propertyType is { IsGenericType: true } genericType
                && (typeof(System.Collections.IEnumerable).IsAssignableFrom(genericType)))
            {
                foreach (var argument in genericType.GetGenericArguments())
                {
                    var nested = FindViolation(argument, depth + 1, seen);
                    if (nested is not null)
                        return nested;
                }
                continue;
            }

            var violation = FindViolation(propertyType, depth + 1, seen);
            if (violation is not null)
                return violation;
        }

        return null;
    }
}
