using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HR.Modules.Identity.Services;

// Mirrors HR.Modules.Companies.Services.StripeGateway's DI/HTTP-client shape, but talks to
// Supabase's Auth Admin API over raw HTTP (there is no first-party .NET SDK equivalent to
// Stripe.net used here). This is a genuinely new, live, untested-against-real-Supabase code path —
// every request/response shape below is a best-effort reading of Supabase's documented Auth API
// conventions and is explicitly flagged as unverified where relevant. Errors are surfaced with the
// raw response body rather than swallowed, since silent failures here would be much harder to
// diagnose than a clear exception during initial end-to-end testing.
internal sealed class SupabaseAuthGateway(IHttpClientFactory httpClientFactory, IOptions<SupabaseAuthOptions> options)
    : ISupabaseAuthGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> CreateUserAsync(string email, string redirectTo, CancellationToken cancellationToken)
    {
        var http = CreateClient(options.Value.SecretKey);

        // UNVERIFIED: Supabase's /auth/v1/invite endpoint creates a user and sends an invite email.
        // The exact placement of the post-verification redirect target ("redirect_to") is not
        // confirmed against this project's live behaviour — Supabase's documented convention for
        // several Auth endpoints nests it under "options": { "redirect_to": ... }, which is what's
        // used here, but it is equally plausible Supabase expects a top-level "redirect_to" or a
        // "data" object instead. Verify against the real project before relying on this in
        // production (see the plan's "Known unverified assumptions" section).
        var requestBody = new
        {
            email,
            options = new { redirect_to = redirectTo },
        };

        using var response = await http.PostAsJsonAsync("/auth/v1/invite", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase invite request failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SupabaseInviteResponse>(JsonOptions, cancellationToken);
        if (payload is null || !Guid.TryParse(payload.Id, out var userId))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase invite response did not contain a parseable user id. Response body: {body}");
        }

        return userId;
    }

    public async Task ResendVerificationEmailAsync(string email, string redirectTo, CancellationToken cancellationToken)
    {
        var http = CreateClient(options.Value.SecretKey);

        // UNVERIFIED — HIGH RISK: Supabase's documented /auth/v1/resend endpoint is paired with the
        // client-facing signUp flow ("type": "signup"), not the admin-initiated /auth/v1/invite flow
        // used by CreateUserAsync above. Whether /auth/v1/resend also works for invite-originated
        // pending users (as opposed to, e.g., needing a second call to /auth/v1/invite for the same
        // email) is explicitly unverified per the plan. This implementation uses the most plausible
        // shape ("type": "signup") as a starting point; it may need to be swapped for a second
        // /auth/v1/invite call once real Supabase behaviour is confirmed. Kept as its own gateway
        // method specifically so that swap is a small, isolated change.
        var requestBody = new
        {
            type = "signup",
            email,
            options = new { redirect_to = redirectTo },
        };

        using var response = await http.PostAsJsonAsync("/auth/v1/resend", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase resend-verification request failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
        }
    }

    public async Task RequestPasswordResetAsync(string email, string redirectTo, CancellationToken cancellationToken)
    {
        // Uses the PUBLISHABLE key, not the secret key — Supabase's /auth/v1/recover is the
        // client-facing password-recovery endpoint (same tier as the sign-in/token endpoints),
        // not an Admin API call. UNVERIFIED: redirect_to nested under "options", mirroring
        // CreateUserAsync/ResendVerificationEmailAsync's shape above — not confirmed against a
        // live project for this specific endpoint.
        var http = CreateClient(options.Value.PublishableKey);

        var requestBody = new
        {
            email,
            options = new { redirect_to = redirectTo },
        };

        using var response = await http.PostAsJsonAsync("/auth/v1/recover", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase password-recovery request failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
        }
    }

    public async Task<SupabaseSession> ExchangeCodeForSessionAsync(string code, CancellationToken cancellationToken)
    {
        // Uses the PUBLISHABLE key (not the secret key) — this is the client-facing token exchange,
        // matching Supabase's convention that the secret key is reserved for server-only Admin API
        // calls (CreateUserAsync/ResendVerificationEmailAsync above).
        var http = CreateClient(options.Value.PublishableKey);

        // UNVERIFIED — SINGLE RISKIEST ASSUMPTION IN THIS GATEWAY: this app is server-rendered
        // (Blazor Server), so the "PKCE flow" here is really just Supabase's redirect-with-code
        // mechanism rather than a classic client-side PKCE dance with a locally-stored
        // code_verifier. Supabase's documented PKCE token exchange is
        // POST {ProjectUrl}/auth/v1/token?grant_type=pkce with body { "auth_code": code } — used
        // here — but real Supabase PKCE may also require a "code_verifier" that would need to have
        // been generated and persisted (e.g. server-side session/cache) at invite time and matched
        // back up here. Because this app never runs a classic PKCE client flow, no code_verifier is
        // generated or sent; if Supabase's live behaviour rejects the exchange without one, this is
        // the first place to look. Must be verified against the real project before Phase D's
        // VerifyEmail handler is exercised end-to-end.
        var requestBody = new { auth_code = code };

        using var response = await http.PostAsJsonAsync("/auth/v1/token?grant_type=pkce", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase PKCE code exchange failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SupabaseTokenResponse>(JsonOptions, cancellationToken);
        if (payload?.AccessToken is null || payload.RefreshToken is null || payload.User?.Id is null
            || !Guid.TryParse(payload.User.Id, out var userId))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase PKCE code exchange response was missing expected fields. Response body: {body}");
        }

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn ?? 3600);
        return new SupabaseSession(payload.AccessToken, payload.RefreshToken, userId, expiresAt);
    }

    // Shared fixed password for all Development dev-persona Supabase Auth users (see
    // EnsureDevUserAsync/SignInWithPasswordAsync and IdentityModule.SeedDevSupabaseUsersAsync).
    // Never used outside Development — real environments never call these methods.
    public const string DevSupabasePassword = "Dev-Only-Password-1!";

    public async Task<Guid> EnsureDevUserAsync(string email, string password, CancellationToken cancellationToken)
    {
        var http = CreateClient(options.Value.SecretKey);

        // UNVERIFIED: Supabase's Admin API for directly creating a confirmed user is
        // POST /auth/v1/admin/users with { email, password, email_confirm: true }. Used here purely
        // for dev-persona seeding (never sends an email, unlike CreateUserAsync's /auth/v1/invite).
        var requestBody = new
        {
            email,
            password,
            email_confirm = true,
        };

        using var response = await http.PostAsJsonAsync("/auth/v1/admin/users", requestBody, JsonOptions, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<SupabaseInviteResponse>(JsonOptions, cancellationToken);
            if (created is null || !Guid.TryParse(created.Id, out var createdId))
            {
                var createdBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Supabase admin create-user response did not contain a parseable user id. Response body: {createdBody}");
            }

            return createdId;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Treat "already exists" as success — this method must be idempotent (called on every
        // Development startup). Supabase's documented error for a duplicate email is typically a
        // 422 (or 400) with a message/error_code mentioning "already been registered" /
        // "email_exists" — matched loosely here since the exact shape is unverified against a live
        // project.
        var isAlreadyExists =
            body.Contains("already", StringComparison.OrdinalIgnoreCase)
            && body.Contains("regist", StringComparison.OrdinalIgnoreCase)
            || body.Contains("email_exists", StringComparison.OrdinalIgnoreCase)
            || body.Contains("user_already_exists", StringComparison.OrdinalIgnoreCase);

        if (!isAlreadyExists)
        {
            throw new InvalidOperationException(
                $"Supabase admin create-user request failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
        }

        // The create call above returns nothing usable on a duplicate, but callers need the SAME
        // Supabase user id that will actually end up in the "sub" claim of tokens issued for this
        // dev persona (to link/verify a UserProfile row) — so resolve it via the exact same
        // password-grant sign-in SignInWithPasswordAsync uses, rather than a separate, unverified
        // admin list-users lookup (that endpoint's filtering behaviour turned out not to reliably
        // return the same id as the one tokens are actually issued with — confirmed via live
        // diagnosis: UserProfile rows seeded from that lookup didn't match the real "sub" claim).
        var session = await SignInWithPasswordAsync(email, password, cancellationToken);
        return session.UserId;
    }

    public async Task<SupabaseSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken)
    {
        // Uses the PUBLISHABLE key — this is the client-facing password grant, matching Supabase's
        // convention that the secret key is reserved for server-only Admin API calls.
        var http = CreateClient(options.Value.PublishableKey);

        var requestBody = new { email, password };

        using var response = await http.PostAsJsonAsync("/auth/v1/token?grant_type=password", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase password-grant sign-in failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SupabaseTokenResponse>(JsonOptions, cancellationToken);
        if (payload?.AccessToken is null || payload.RefreshToken is null || payload.User?.Id is null
            || !Guid.TryParse(payload.User.Id, out var userId))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase password-grant sign-in response was missing expected fields. Response body: {body}");
        }

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn ?? 3600);
        return new SupabaseSession(payload.AccessToken, payload.RefreshToken, userId, expiresAt);
    }

    public async Task UpdatePasswordAsync(string userAccessToken, string newPassword, CancellationToken cancellationToken)
    {
        var http = CreateUserScopedClient(userAccessToken);

        var requestBody = new { password = newPassword };

        using var response = await http.PutAsJsonAsync("/auth/v1/user", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase update-password request failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
        }
    }

    private HttpClient CreateClient(string apiKey)
    {
        var http = httpClientFactory.CreateClient(nameof(SupabaseAuthGateway));
        http.BaseAddress = new Uri(options.Value.ProjectUrl);
        http.DefaultRequestHeaders.Remove("apikey");
        http.DefaultRequestHeaders.Add("apikey", apiKey);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return http;
    }

    /// <summary>
    /// Unlike CreateClient above (which sends the same value as both "apikey" and the Bearer
    /// token — the publishable/secret keys authenticate the *caller* to Supabase's Auth API),
    /// a user-scoped endpoint like PUT /auth/v1/user needs "apikey" to stay the publishable key
    /// while Authorization carries the *user's own* access token, so Supabase knows which
    /// account's password to update.
    /// </summary>
    private HttpClient CreateUserScopedClient(string userAccessToken)
    {
        var http = httpClientFactory.CreateClient(nameof(SupabaseAuthGateway));
        http.BaseAddress = new Uri(options.Value.ProjectUrl);
        http.DefaultRequestHeaders.Remove("apikey");
        http.DefaultRequestHeaders.Add("apikey", options.Value.PublishableKey);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userAccessToken);
        return http;
    }

    private sealed class SupabaseInviteResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    private sealed class SupabaseTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }

        [JsonPropertyName("user")]
        public SupabaseUserPayload? User { get; set; }
    }

    private sealed class SupabaseUserPayload
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }
}
