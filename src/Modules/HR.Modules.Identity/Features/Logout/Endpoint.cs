using System.Net.Http.Headers;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace HR.Modules.Identity.Features.Logout;

internal sealed class Endpoint(
    LogoutHandler handler) : EndpointWithoutRequest<LogoutResponse>
{
    public override void Configure()
    {
        Post("/api/logout");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        string? accessToken = null;
        if (HttpContext.Request.Headers.TryGetValue(HeaderNames.Authorization, out var header)
            && AuthenticationHeaderValue.TryParse(header.ToString(), out var parsed)
            && string.Equals(parsed.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            accessToken = parsed.Parameter;
        }

        Guid? supabaseAuthUserId = null;
        if (HttpContext.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(HttpContext.User.FindFirst("sub")?.Value, out var parsedUserId))
        {
            supabaseAuthUserId = parsedUserId;
        }

        var result = await handler.HandleAsync(accessToken, supabaseAuthUserId, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
