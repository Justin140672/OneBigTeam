using System.Diagnostics;
using System.Text.RegularExpressions;
using HR.SharedKernel.ExecutionContext;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace HR.Infrastructure.Logging;

/// <summary>
/// Extracts X-Correlation-ID from the incoming request (or generates one) and:
/// - validates the caller-supplied value against a length/character policy, rejecting and
///   replacing anything malformed/oversized before it ever reaches logs or persistence (ticket 23)
/// - stores it in HttpContext.Items for downstream middleware
/// - echoes it back in the response header
/// - pushes it into Serilog's LogContext so every log within the request includes it
/// - establishes the request's <see cref="IExecutionContext"/> (Origin = HttpRequest) as ambient,
///   so a command that publishes integration events / writes audit or outbox records during this
///   request carries the same correlation id through all of them
/// - tags the active distributed-tracing Activity with the correlation id without treating it as
///   (i.e. without conflating it with) the trace id itself
///
/// Design note (ticket 23 follow-up): correlation ids are accepted as ANY caller-supplied string
/// that passes a length + character allow-list policy — NOT only well-formed GUIDs. Earlier this
/// middleware required GUID format, which broke real callers/tests supplying opaque prefixed ids
/// (e.g. "e2e-&lt;guid&gt;") that the ticket's own acceptance criteria ("accept a valid
/// caller-supplied X-Correlation-ID within a documented length/character policy") always intended
/// to allow. <see cref="IExecutionContext.CorrelationId"/> is therefore a <see cref="string"/>, not
/// a <see cref="Guid"/> — see that type's remarks. Where a Guid-typed persistence column still needs
/// a value (e.g. <c>IAuditEvent.CorrelationId</c>, outbox/operation table <c>correlation_id</c>
/// columns), <see cref="CorrelationIdGuid.Derive"/> maps this string to a stable Guid: unchanged for
/// an already-GUID-shaped string (the common case), deterministically hashed otherwise.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemsKey = "CorrelationId";

    /// <summary>Documented length policy: long enough for prefixed/namespaced ids (e.g.
    /// "e2e-&lt;guid&gt;", "trace-&lt;...&gt;"), short enough to keep logs/headers/persistence
    /// bounded and to make header-injection/DoS-via-oversized-header attempts cheap to reject.</summary>
    private const int MaxHeaderLength = 128;

    /// <summary>Documented character policy: ASCII alphanumerics plus '-', '_', '.', ':' — enough for
    /// GUIDs, ULIDs, and common "prefix-id" conventions, while excluding anything that could enable
    /// log/header injection (newlines, control characters, delimiters) or be otherwise unsafe to
    /// echo back verbatim into an HTTP header and structured logs.
    /// Anchored with <c>\z</c>, not <c>$</c>: in .NET <c>$</c> also matches immediately before a
    /// trailing newline, which would let a value such as "abc" + LF through the allow-list.</summary>
    private static readonly Regex AllowedCharacters = new(@"^[A-Za-z0-9._:-]+\z", RegexOptions.Compiled);

    public async Task InvokeAsync(HttpContext context, IExecutionContextAccessor executionContextAccessor)
    {
        var supplied = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = IsAcceptable(supplied)
            ? supplied!
            : Guid.NewGuid().ToString("D");

        context.Items[ItemsKey] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Associate with the active trace for cross-referencing in tracing backends, without
        // conflating correlation identity with trace identity — the trace id remains
        // Activity.Current?.TraceId; this is only a tag alongside it.
        Activity.Current?.SetTag("correlation.id", correlationId);

        // Root context for an HTTP request always uses the resolved (accepted-or-generated)
        // correlation id verbatim, so downstream audit/outbox records trace back to exactly what the
        // caller (or this middleware, if none/invalid was supplied) established.
        var executionContext = new ExecutionContextInfo(
            CorrelationId: correlationId,
            MessageId: Guid.NewGuid(),
            CausationId: null,
            TraceId: Activity.Current?.TraceId.ToString(),
            ActorUserId: null,
            ActorEmployeeId: null,
            ActorType: HR.SharedKernel.AuditActorType.Human,
            Origin: ExecutionOrigin.HttpRequest);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (executionContextAccessor.Push(executionContext))
        {
            await next(context);
        }
    }

    /// <summary>Length + character allow-list applied to a caller-supplied correlation id (CodeQL #63-#65, #67).</summary>
    internal static bool IsAcceptable(string? supplied) =>
        !string.IsNullOrWhiteSpace(supplied)
        && supplied.Length <= MaxHeaderLength
        && AllowedCharacters.IsMatch(supplied);
}
