using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HR.SharedKernel;
using HR.Modules.Identity.Services.AccountEmailPolicy;
using Microsoft.Extensions.Options;

namespace HR.Modules.Identity.Services;

internal sealed class SupabaseAuthGateway(
    IHttpClientFactory httpClientFactory,
    IOptions<SupabaseAuthOptions> options,
    // Ticket 9: optional only so direct constructions (unit tests) keep compiling; falls back to
    // the embedded baseline denylist, never to "allow everything". DI always supplies the
    // configured singleton.
    IAccountEmailDomainPolicy? accountEmailDomainPolicy = null)
    : ISupabaseAuthGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Ticket 9: last line of defence for the account-creation email-domain policy — every method
    /// that creates a NEW identity-provider account calls this before any HTTP request, so a future
    /// account-creation path that forgets <see cref="AccountCreationEmailGuard"/> still cannot
    /// create a Gmail/Hotmail/disposable-domain account. Never applied to sign-in, password reset
    /// or lookups, so existing accounts on those domains keep working.
    /// </summary>
    private void EnsureAccountEmailPermitted(string email)
    {
        var evaluation = (accountEmailDomainPolicy ?? AccountEmailDomainPolicy.Default).Evaluate(email);
        if (!evaluation.IsAllowed)
            throw new AccountEmailDomainNotPermittedException(evaluation.Domain);
    }

    private static readonly HashSet<string> SafeErrorFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "error", "error_description", "error_code", "code", "msg", "message",
    };

    private static string Describe(string? rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return "response body: (none)";

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                        continue;
                    if (!SafeErrorFields.Contains(property.Name))
                        continue;
                    parts.Add($"{property.Name}={SensitiveDataScrubber.ScrubText(property.Value.ToString())}");
                }

                if (parts.Count > 0)
                    return $"response detail: {string.Join(", ", parts)}";
            }
        }
        catch (JsonException)
        {
        }

        return "response body: (redacted)";
    }

    public async Task<Guid> CreateUserAsync(string email, string password, string redirectTo, CancellationToken cancellationToken)
    {
        EnsureAccountEmailPermitted(email);

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
                $"Supabase admin create-user request failed with status {(int)response.StatusCode} ({response.StatusCode}). {Describe(body)}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SupabaseInviteResponse>(JsonOptions, cancellationToken);
        if (payload is null || !Guid.TryParse(payload.Id, out var userId))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase admin create-user response did not contain a parseable user id. {Describe(body)}");
        }

        await ResendVerificationEmailAsync(email, redirectTo, cancellationToken);

        return userId;
    }

    public async Task ResendVerificationEmailAsync(string email, string redirectTo, CancellationToken cancellationToken)
    {
        var http = CreateClient(options.Value.SecretKey);

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
                $"Supabase resend-verification request failed with status {(int)response.StatusCode} ({response.StatusCode}). {Describe(body)}");
        }
    }

    public async Task RequestPasswordResetAsync(string email, string redirectTo, CancellationToken cancellationToken)
    {
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
                $"Supabase password-recovery request failed with status {(int)response.StatusCode} ({response.StatusCode}). {Describe(body)}");
        }
    }

    public async Task<string> GenerateRecoveryLinkAsync(string email, string redirectTo, CancellationToken cancellationToken)
    {
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
                $"Supabase generate-recovery-link request failed with status {(int)response.StatusCode} ({response.StatusCode}). {Describe(body)}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SupabaseGenerateLinkResponse>(JsonOptions, cancellationToken);
        if (string.IsNullOrWhiteSpace(payload?.ActionLink))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase generate-recovery-link response did not contain an action_link. {Describe(body)}");
        }

        return payload.ActionLink;
    }

    public async Task<SupabaseSession> ExchangeCodeForSessionAsync(string code, CancellationToken cancellationToken)
    {
        var http = CreateClient(options.Value.PublishableKey);

        var requestBody = new { auth_code = code };

        using var response = await http.PostAsJsonAsync("/auth/v1/token?grant_type=pkce", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase PKCE code exchange failed with status {(int)response.StatusCode} ({response.StatusCode}). {Describe(body)}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SupabaseTokenResponse>(JsonOptions, cancellationToken);
        if (payload?.AccessToken is null || payload.RefreshToken is null || payload.User?.Id is null
            || !Guid.TryParse(payload.User.Id, out var userId))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase PKCE code exchange response was missing expected fields. {Describe(body)}");
        }

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn ?? 3600);
        return new SupabaseSession(payload.AccessToken, payload.RefreshToken, userId, expiresAt);
    }

    public const string DevSupabasePassword = "Dev-Only-Password-1!";

    public async Task<Guid> EnsureDevUserAsync(string email, string password, CancellationToken cancellationToken)
    {
        var http = CreateClient(options.Value.SecretKey);

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
                    $"Supabase admin create-user response did not contain a parseable user id. {Describe(createdBody)}");
            }

            return createdId;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        var isAlreadyExists =
            body.Contains("already", StringComparison.OrdinalIgnoreCase)
            && body.Contains("regist", StringComparison.OrdinalIgnoreCase)
            || body.Contains("email_exists", StringComparison.OrdinalIgnoreCase)
            || body.Contains("user_already_exists", StringComparison.OrdinalIgnoreCase);

        if (!isAlreadyExists)
        {
            throw new InvalidOperationException(
                $"Supabase admin create-user request failed with status {(int)response.StatusCode} ({response.StatusCode}). {Describe(body)}");
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

    public async Task<Guid> CreateConfirmedUserAsync(
        string email, string password, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        EnsureAccountEmailPermitted(email);

        var http = CreateClient(options.Value.SecretKey);

        object requestBody = metadata is { Count: > 0 }
            ? new
            {
                email,
                password,
                email_confirm = true,
                user_metadata = metadata,
            }
            : new
            {
                email,
                password,
                email_confirm = true,
            };

        using var response = await http.PostAsJsonAsync("/auth/v1/admin/users", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

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
                $"Supabase admin create-user request failed with status {(int)response.StatusCode} ({response.StatusCode}). {Describe(body)}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SupabaseInviteResponse>(JsonOptions, cancellationToken);
        if (payload is null || !Guid.TryParse(payload.Id, out var userId))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase admin create-user response did not contain a parseable user id. {Describe(body)}");
        }

        return userId;
    }

    public async Task<SupabaseSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken)
    {
        var http = CreateClient(options.Value.PublishableKey);

        var requestBody = new { email, password };

        using var response = await http.PostAsJsonAsync("/auth/v1/token?grant_type=password", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase password-grant sign-in failed with status {(int)response.StatusCode} ({response.StatusCode}). {Describe(body)}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SupabaseTokenResponse>(JsonOptions, cancellationToken);
        if (payload?.AccessToken is null || payload.RefreshToken is null || payload.User?.Id is null
            || !Guid.TryParse(payload.User.Id, out var userId))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase password-grant sign-in response was missing expected fields. {Describe(body)}");
        }

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn ?? 3600);
        return new SupabaseSession(payload.AccessToken, payload.RefreshToken, userId, expiresAt);
    }

    public async Task<int> RemoveAllMfaFactorsAsync(Guid supabaseUserId, CancellationToken cancellationToken)
    {
        var http = CreateClient(options.Value.SecretKey);

        using var listResponse = await http.GetAsync(
            $"/auth/v1/admin/users/{supabaseUserId}/factors", cancellationToken);

        if (!listResponse.IsSuccessStatusCode)
        {
            var body = await listResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase list-MFA-factors request failed with status {(int)listResponse.StatusCode} ({listResponse.StatusCode}). {Describe(body)}");
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
                    $"Supabase delete-MFA-factor request failed with status {(int)deleteResponse.StatusCode} ({deleteResponse.StatusCode}). {Describe(body)}");
            }

            removed++;
        }

        return removed;
    }

    public async Task<Guid?> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var http = CreateClient(options.Value.SecretKey);
        var normalized = email.Trim().ToLowerInvariant();

        using var response = await http.GetAsync(
            $"/auth/v1/admin/users?filter={Uri.EscapeDataString(normalized)}&per_page=200", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase list-users request failed with status {(int)response.StatusCode} ({response.StatusCode}). {Describe(body)}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SupabaseAdminUsersResponse>(JsonOptions, cancellationToken);

        var match = payload?.Users?.FirstOrDefault(u =>
            string.Equals(u.Email?.Trim(), normalized, StringComparison.OrdinalIgnoreCase));

        return match?.Id is { } id && Guid.TryParse(id, out var userId) ? userId : null;
    }

    public async Task<(Guid UserId, IReadOnlyDictionary<string, string> Metadata)?> GetUserMetadataByEmailAsync(
        string email, CancellationToken cancellationToken)
    {
        // Ticket 12 (P1): same admin list-by-email lookup as GetUserIdByEmailAsync, but also
        // surfaces user_metadata so a caller can verify a specific provisioning correlation value
        // rather than trusting the mere existence of a matching account.
        var http = CreateClient(options.Value.SecretKey);
        var normalized = email.Trim().ToLowerInvariant();

        using var response = await http.GetAsync(
            $"/auth/v1/admin/users?filter={Uri.EscapeDataString(normalized)}&per_page=200", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase list-users request failed with status {(int)response.StatusCode} ({response.StatusCode}). {Describe(body)}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SupabaseAdminUsersResponse>(JsonOptions, cancellationToken);

        var match = payload?.Users?.FirstOrDefault(u =>
            string.Equals(u.Email?.Trim(), normalized, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is not { } id || !Guid.TryParse(id, out var userId))
            return null;

        var metadata = new Dictionary<string, string>();
        if (match.UserMetadata is not null)
        {
            foreach (var property in match.UserMetadata)
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                    metadata[property.Key] = property.Value.GetString() ?? string.Empty;
            }
        }

        return (userId, metadata);
    }

    public async Task<Guid> CreatePendingUserWithMetadataAsync(
        string email, string redirectTo, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken)
    {
        EnsureAccountEmailPermitted(email);

        var http = CreateClient(options.Value.SecretKey);

        // Random password this app never persists or returns — the recipient authenticates for the
        // first time via Supabase's own confirmation-email link + a subsequent password set/reset,
        // never via a password this process ever knew. Same admin CREATE endpoint as CreateUserAsync
        // (see its remarks for why /auth/v1/invite is deliberately not used).
        var randomPassword = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

        var requestBody = new
        {
            email,
            password = randomPassword,
            email_confirm = false,
            user_metadata = metadata,
        };

        using var response = await http.PostAsJsonAsync("/auth/v1/admin/users", requestBody, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            var isAlreadyExists =
                body.Contains("already", StringComparison.OrdinalIgnoreCase)
                && body.Contains("regist", StringComparison.OrdinalIgnoreCase)
                || body.Contains("email_exists", StringComparison.OrdinalIgnoreCase)
                || body.Contains("user_already_exists", StringComparison.OrdinalIgnoreCase);

            if (isAlreadyExists)
                throw new EmailAlreadyRegisteredException(email);

            throw new InvalidOperationException(
                $"Supabase admin create-user request failed with status {(int)response.StatusCode} ({response.StatusCode}). {Describe(body)}");
        }

        var payload = await response.Content.ReadFromJsonAsync<SupabaseInviteResponse>(JsonOptions, cancellationToken);
        if (payload is null || !Guid.TryParse(payload.Id, out var userId))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase admin create-user response did not contain a parseable user id. {Describe(body)}");
        }

        await ResendVerificationEmailAsync(email, redirectTo, cancellationToken);

        return userId;
    }

    private sealed record SupabaseAdminUsersResponse(List<SupabaseAdminUser>? Users);

    private sealed record SupabaseAdminUser(
        string? Id, string? Email,
        [property: JsonPropertyName("user_metadata")] Dictionary<string, JsonElement>? UserMetadata);

    public async Task SignOutAsync(string userAccessToken, CancellationToken cancellationToken)
    {
        var http = CreateUserScopedClient(userAccessToken);

        using var response = await http.PostAsync("/auth/v1/logout?scope=global", content: null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Supabase sign-out request failed with status {(int)response.StatusCode} ({response.StatusCode}). {Describe(body)}");
        }
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
                $"Supabase update-password request failed with status {(int)response.StatusCode} ({response.StatusCode}). {Describe(body)}");
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
