using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace HR.SharedKernel.Http;

/// <summary>
/// Small, validating builder/parser for <c>Content-Security-Policy</c> header values, shared by
/// HR.Web and HR.Admin.Web. It deliberately carries NO policy of its own — each app keeps its own
/// explicit directive list (<c>HrWebContentSecurityPolicy</c>, <c>AdminContentSecurityPolicy</c>);
/// this type only guarantees the mechanics are right:
/// <list type="bullet">
/// <item>directive names are lowercase CSP tokens and each directive appears once (browsers silently
/// ignore every repeat of a directive, so a duplicate would quietly drop the sources it adds);</item>
/// <item>source expressions cannot contain whitespace, <c>;</c> or <c>,</c> — so a configured origin
/// or a request-derived host can never smuggle in an extra source or directive;</item>
/// <item>nonces are 128-bit CSPRNG values, base64 encoded, fresh per call.</item>
/// </list>
/// </summary>
public sealed partial class ContentSecurityPolicyBuilder
{
    private readonly List<KeyValuePair<string, string[]>> _directives = [];

    /// <summary>Appends a directive. Throws if the directive already exists or any source is malformed.</summary>
    public ContentSecurityPolicyBuilder Add(string directive, params IEnumerable<string> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directive);
        ArgumentNullException.ThrowIfNull(sources);

        // Normalize to lowercase for consistent storage and comparison (CSP directives are case-insensitive).
        directive = directive.ToLowerInvariant();

        if (!DirectiveName().IsMatch(directive))
            throw new ArgumentException($"'{directive}' is not a valid CSP directive name.", nameof(directive));

        if (_directives.Any(d => d.Key == directive))
            throw new InvalidOperationException($"CSP directive '{directive}' has already been added.");

        var list = sources.ToArray();
        foreach (var source in list)
        {
            if (string.IsNullOrEmpty(source) || source.Any(c => char.IsWhiteSpace(c) || c is ';' or ','))
                throw new ArgumentException($"'{source}' is not a valid CSP source expression for '{directive}'.", nameof(sources));
        }

        _directives.Add(new(directive, list));
        return this;
    }

    /// <summary>Serialises the directives in the order they were added: <c>name src src; name src</c>.</summary>
    public string Build() =>
        string.Join("; ", _directives.Select(d => d.Value.Length == 0 ? d.Key : $"{d.Key} {string.Join(' ', d.Value)}"));

    /// <summary>A fresh 128-bit, base64-encoded nonce from the platform CSPRNG.</summary>
    public static string CreateNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    /// <summary>The <c>'nonce-…'</c> source expression for <paramref name="nonce"/>.</summary>
    public static string NonceSource(string nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        if (!Base64Value().IsMatch(nonce))
            throw new ArgumentException("A CSP nonce must be a base64 value.", nameof(nonce));
        return $"'nonce-{nonce}'";
    }

    /// <summary>
    /// Parses a serialised policy into <c>directive → sources</c> (directive names lower-cased).
    /// Throws <see cref="FormatException"/> for a repeated directive, which browsers would ignore.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Parse(string policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var directive in policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = directive.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var name = parts[0].ToLowerInvariant();
            if (!result.TryAdd(name, parts[1..]))
                throw new FormatException($"CSP directive '{name}' appears more than once.");
        }

        return result;
    }

    [GeneratedRegex("^[a-z][a-z-]*$")]
    private static partial Regex DirectiveName();

    [GeneratedRegex("^[A-Za-z0-9+/_-]+={0,2}$")]
    private static partial Regex Base64Value();
}
