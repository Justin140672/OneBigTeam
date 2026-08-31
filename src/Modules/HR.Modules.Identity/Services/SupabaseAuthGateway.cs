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

    public async Task<Guid> CreateUserAsync(string email, string password, string redirectTo, CancellationToken cancellationToken)
    {
        var http = CreateClient(options.Value.SecretKey);

        // Deliberately NOT /auth/v1/invite: confirmed via live diagnosis that invite-created users
        // don't get a real email/password identity wired up — a follow-up admin PUT to set the
        // password looked like it succeeded (200 response) but real Supabase password-grant sign-in
        // still failed with "Invalid login credentials" afterward. Using the same admin CREATE
        // endpoint as EnsureDevUserAsync below (with the real password baked in from the start and
        // email_confirm: false so the account still requires verification) avoids that entirely —
        // this is the one Admin API shape already proven to work end-to-end for password auth here.
        var requestBody = new
        {
            email,
            password,
            email_confirm = false,
        };

        using var response = await http.PostAsJsonAsync("/auth/v1/admin/users", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // Same loose matching as EnsureDevUserAsync below: Supabase's duplicate-email error is
            // typically a 422 (or 400) with a message/error_code mentioning "already been
            // registered" / "email_exists" / "user_already_exists" — this can happen here even
            // though SignUpHandler already checked its own tables first, e.g. a Supabase user left
            // over from a prior signup attempt whose local UserProfile never got created.
            var isAlreadyExists =
                body.Contains("already", StringComparison.OrdinalIgnoreCase)
                && body.Contains("regist", StringComparison.OrdinalIgnoreCase)
                || body.Contains("email_exists", StringComparison.OrdinalIgnoreCase)
                || body.Contains("user_already_exists", StringComparison.OrdinalIgnoreCase);

            if (isAlreadyExists)
            {
                throw new EmailAlreadyRegisteredException(email);
            }

            throw new InvalidOperationException(
                $"Supabase admin create-user request failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SupabaseInviteResponse>(JsonOptions, cancellationToken);
        if (payload is null || !Guid.TryParse(payload.Id, out var userId))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase admin create-user response did not contain a parseable user id. Response body: {body}");
        }

        // The admin create call above never sends an email itself — /auth/v1/resend is what
        // actually delivers the confirmation link (already used identically by
        // ResendVerificationEmailAsync for the "user asked us to resend it" case).
        await ResendVerificationEmailAsync(email, redirectTo, cancellationToken);

        return userId;
    }

    public async Task ResendVerificationEmailAsync(string email, string redirectTo, CancellationToken cancellationToken)
    {
        var http = CreateClient(options.Value.SecretKey);

        // "type": "signup" is correct now that CreateUserAsync above creates the pending user via
        // the admin CREATE endpoint (same shape as a normal client signUp, just admin-initiated)
        // rather than /auth/v1/invite — see CreateUserAsync's remarks for why that switch happened.
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
        // Client-facing /auth/v1/recover — Supabase composes and sends the recovery email itself.
        // Retained for the platform administrator reset flow only.
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

    public async Task<string> GenerateRecoveryLinkAsync(string email, string redirectTo, CancellationToken cancellationToken)
    {
        // Admin API — uses the SECRET key (server-only), unlike the client-facing
        // /auth/v1/recover path. Supabase's documented shape is
        // POST /auth/v1/admin/generate_link with { type: "recovery", email, redirect_to } and a
        // response body carrying "action_link" (the ready-to-send URL). No email is sent by
        // Supabase for generate_link — delivery is entirely the caller's responsibility.
        var http = CreateClient(options.Value.SecretKey);

        var requestBody = new
        {
            type = "recovery",
            email,
            redirect_to = redirectTo,
        };

        using var response = await http.PostAsJsonAsync("/auth/v1/admin/generate_link", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase generate-recovery-link request failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SupabaseGenerateLinkResponse>(JsonOptions, cancellationToken);
        if (string.IsNullOrWhiteSpace(payload?.ActionLink))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase generate-recovery-link response did not contain an action_link. Response body: {body}");
        }

        return payload.ActionLink;
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

    public async Task<Guid> CreateConfirmedUserAsync(string email, string password, CancellationToken cancellationToken)
    {
        var http = CreateClient(options.Value.SecretKey);

        var requestBody = new
        {
            email,
            password,
            email_confirm = true,
        };

        using var response = await http.PostAsJsonAsync("/auth/v1/admin/users", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // Same loose matching as CreateUserAsync/EnsureDevUserAsync above.
            var isAlreadyExists =
                body.Contains("already", StringComparison.OrdinalIgnoreCase)
                && body.Contains("regist", StringComparison.OrdinalIgnoreCase)
                || body.Contains("email_exists", StringComparison.OrdinalIgnoreCase)
                || body.Contains("user_already_exists", StringComparison.OrdinalIgnoreCase);

            if (isAlreadyExists)
            {
                throw new EmailAlreadyRegisteredException(email);
            }

            throw new InvalidOperationException(
                $"Supabase admin create-user request failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SupabaseInviteResponse>(JsonOptions, cancellationToken);
        if (payload is null || !Guid.TryParse(payload.Id, out var userId))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase admin create-user response did not contain a parseable user id. Response body: {body}");
        }

        return userId;
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

    public async Task<int> RemoveAllMfaFactorsAsync(Guid supabaseUserId, CancellationToken cancellationToken)
    {
        // Admin API — SECRET key (server-only), same as the other /auth/v1/admin/* calls above.
        var http = CreateClient(options.Value.SecretKey);

        using var listResponse = await http.GetAsync(
            $"/auth/v1/admin/users/{supabaseUserId}/factors", cancellationToken);

        if (!listResponse.IsSuccessStatusCode)
        {
            var body = await listResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase list-MFA-factors request failed with status {(int)listResponse.StatusCode} ({listResponse.StatusCode}). Response body: {body}");
        }

        var factors = await listResponse.Content.ReadFromJsonAsync<List<SupabaseFactor>>(JsonOptions, cancellationToken)
                      ?? [];

        var removed = 0;
        foreach (var factor in factors)
        {
            if (string.IsNullOrWhiteSpace(factor.Id))
                continue;

            using var deleteResponse = await http.DeleteAsync(
                $"/auth/v1/admin/users/{supabaseUserId}/factors/{factor.Id}", cancellationToken);

            if (!deleteResponse.IsSuccessStatusCode)
            {
                var body = await deleteResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Supabase delete-MFA-factor request failed with status {(int)deleteResponse.StatusCode} ({deleteResponse.StatusCode}). Response body: {body}");
            }

            removed++;
        }

        return removed;
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

    private sealed class SupabaseFactor
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    private sealed class SupabaseGenerateLinkResponse
    {
        [JsonPropertyName("action_link")]
        public string? ActionLink { get; set; }
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
