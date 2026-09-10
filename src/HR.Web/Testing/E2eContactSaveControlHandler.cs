#nullable enable
using System.Net;
using System.Text;
using System.Text.Json;

namespace HR.Web.Testing;

// TEST-ONLY (E2E_TESTING-gated) DelegatingHandler on the "hrapi" typed client. Intercepts only the
// PUT .../employees/me/contact-details request and, when the caller's email has a registered
// control, lets an E2E test hold / release / fail that outbound save.
internal sealed class E2eContactSaveControlHandler(E2eContactSaveControlStore store) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Put ||
            request.RequestUri is null ||
            !request.RequestUri.AbsolutePath.EndsWith("/employees/me/contact-details", StringComparison.Ordinal))
        {
            return await base.SendAsync(request, ct).ConfigureAwait(false);
        }

        string? email = null;
        try
        {
            var token = request.Headers.Authorization?.Parameter;
            if (!string.IsNullOrEmpty(token))
            {
                var segments = token.Split('.');
                if (segments.Length >= 2)
                {
                    var payload = Base64UrlDecode(segments[1]);
                    using var doc = JsonDocument.Parse(payload);
                    if (doc.RootElement.TryGetProperty("email", out var emailProp) &&
                        emailProp.ValueKind == JsonValueKind.String)
                    {
                        email = emailProp.GetString();
                    }
                }
            }
        }
        catch
        {
            return await base.SendAsync(request, ct).ConfigureAwait(false);
        }

        if (email is null || !store.TryGet(email.ToLowerInvariant(), out var control))
        {
            return await base.SendAsync(request, ct).ConfigureAwait(false);
        }

        var decision = await control.OnRequestArrivedAsync(ct).ConfigureAwait(false);

        if (decision == "fail")
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    "{\"error\":\"Simulated server failure (E2E).\",\"code\":\"server_error\"}",
                    Encoding.UTF8,
                    "application/json"),
                RequestMessage = request,
            };
        }

        return await base.SendAsync(request, ct).ConfigureAwait(false);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }

        return Convert.FromBase64String(s);
    }
}
