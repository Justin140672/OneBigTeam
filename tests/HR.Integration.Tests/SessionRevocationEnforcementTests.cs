using System.Net;
using System.Net.Http.Headers;
using HR.Api.Authentication;
using HR.Modules.Identity;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 13 — cross-tab/cross-replica logout enforcement, exercised end-to-end through the real
/// production pieces: <see cref="SupabaseJwtBearerConfiguration"/> (the same JWT bearer wiring
/// HR.Api uses, in its E2E/local-key mode — see <see cref="E2eFakeSupabaseJwt"/>),
/// <see cref="SessionRevocationStore"/> backed by a real PostgreSQL <see cref="IdentityDbContext"/>,
/// and <see cref="IdentityModule.IsSessionRevokedAsync"/> called from
/// <c>OnTokenValidated</c> exactly as HR.Api's real pipeline calls it.
///
/// Deliberately NOT built on <see cref="Infrastructure.ApiWebApplicationFactory"/>: that factory
/// installs <c>TestAuthHandler</c>, which short-circuits the real JwtBearer pipeline entirely (see
/// <see cref="SigningKeyRefreshResilienceTests"/>'s own remarks for the same reason) — a test built
/// on it could only prove a local claims-principal-substitution behaved a certain way, never that a
/// genuine bearer token is actually rejected by real signature/claims validation.
///
/// THE OLD, INADEQUATE PATTERN THIS REPLACES (documented per the ticket): a same-DI-scope test that
/// logs out and then re-asserts against some client-held object (e.g. a Blazor circuit's in-memory
/// "current user" state, or reusing the very same <c>HttpClient</c>/scope that just performed the
/// logout) only proves that ONE local, in-process object was mutated or cleared. It does NOT prove
/// the server itself would reject the old token on a fresh request — a second browser tab, a
/// different replica behind a load balancer, or a curl request replaying a captured token would
/// still get through, and such a test would never catch it. Every assertion below instead issues a
/// brand new, independent <see cref="HttpClient"/> request (a new ASP.NET Core request pipeline
/// execution, with its own DI scope) using nothing but the raw bearer token string captured earlier
/// — there is no shared C# object between "log out" and "prove the old token is now rejected" other
/// than that string, which is exactly what an attacker/second-tab would also only have.
/// </summary>
public sealed class SessionRevocationEnforcementTests : IAsyncLifetime
{
    private const string SupabaseProjectUrl = "https://test-project.supabase.co";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("hr_session_revocation_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private RevocationTestHost _host = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _host = await RevocationTestHost.StartAsync(_postgres.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Token_Is_Rejected_By_A_Fresh_Independent_Request_After_The_Same_Token_Logs_Out()
    {
        var userId = Guid.NewGuid();
        var token = E2eFakeSupabaseJwt.CreateAccessToken(
            SupabaseProjectUrl, userId, "laura.bennett@acme.example", TimeSpan.FromMinutes(30));

        // Step 1: "tab A" — the token is genuinely valid before any logout. A brand new HttpClient,
        // used only for this one call.
        using (var beforeLogoutClient = _host.CreateClient())
        {
            var beforeLogoutResponse = await GetSecureAsync(beforeLogoutClient, token);
            Assert.Equal(HttpStatusCode.OK, beforeLogoutResponse.StatusCode);
        }

        // Step 2: "tab A logs out" — a completely separate HTTP request/response cycle (its own
        // fresh HttpClient and its own server-side DI scope), presenting the SAME token as the
        // bearer, exactly as HR.Web's /logout does with the cookie's access token.
        using (var logoutClient = _host.CreateClient())
        {
            var logoutResponse = await PostLogoutAsync(logoutClient, token);
            Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
        }

        // Step 3: "tab B" — a brand new HttpClient/request that never touched the logout call above,
        // replaying the ORIGINAL (pre-logout) token. This is the assertion that actually proves
        // server-side enforcement: nothing but the raw token string survived from step 1 to here.
        using var afterLogoutClient = _host.CreateClient();
        var afterLogoutResponse = await GetSecureAsync(afterLogoutClient, token);

        Assert.Equal(HttpStatusCode.Unauthorized, afterLogoutResponse.StatusCode);
    }

    [Fact]
    public async Task A_Token_Never_Presented_To_Logout_Is_Also_Rejected_For_The_Same_User_After_Logout()
    {
        // Per-user, not per-token, revocation semantics (see SessionRevocation's class remarks and
        // IdentityModule.IsSessionRevokedAsync). Simulates "tab B" holding a distinct access token for
        // the same underlying Supabase user/session that was never itself sent to /api/logout — e.g.
        // a token refreshed slightly earlier than tab A's — and must still be rejected once any token
        // for that user has triggered a logout, because SessionRevocation is keyed purely by
        // SupabaseAuthUserId, never by the token string itself.
        var userId = Guid.NewGuid();
        var tokenA = E2eFakeSupabaseJwt.CreateAccessToken(
            SupabaseProjectUrl, userId, "laura.bennett@acme.example", TimeSpan.FromMinutes(30));
        // A distinct token string for the same user id, issued a moment later — JwtSecurityTokenHandler
        // stamps "iat"/"nbf" from DateTime.UtcNow, so a short delay guarantees a different signature
        // even though every claim value besides timing is identical.
        await Task.Delay(1100);
        var tokenB = E2eFakeSupabaseJwt.CreateAccessToken(
            SupabaseProjectUrl, userId, "laura.bennett@acme.example", TimeSpan.FromMinutes(30));
        Assert.NotEqual(tokenA, tokenB);

        using (var beforeLogoutClient = _host.CreateClient())
        {
            Assert.Equal(HttpStatusCode.OK, (await GetSecureAsync(beforeLogoutClient, tokenB)).StatusCode);
        }

        using (var logoutClient = _host.CreateClient())
        {
            Assert.Equal(HttpStatusCode.OK, (await PostLogoutAsync(logoutClient, tokenA)).StatusCode);
        }

        using var afterLogoutClient = _host.CreateClient();
        var response = await GetSecureAsync(afterLogoutClient, tokenB);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_Without_A_Recognised_Bearer_Does_Not_Revoke_Anyone_Elses_Session()
    {
        var userId = Guid.NewGuid();
        var token = E2eFakeSupabaseJwt.CreateAccessToken(
            SupabaseProjectUrl, userId, "laura.bennett@acme.example", TimeSpan.FromMinutes(30));

        // A logout call with no bearer at all must still succeed (HR.Web clears its cookie
        // regardless — see Endpoint.cs remarks) but must not revoke any session, since there is no
        // "sub" claim to revoke against.
        using (var anonymousLogoutClient = _host.CreateClient())
        {
            var response = await anonymousLogoutClient.PostAsync("/api/logout", content: null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var client = _host.CreateClient();
        var secureResponse = await GetSecureAsync(client, token);

        Assert.Equal(HttpStatusCode.OK, secureResponse.StatusCode);
    }

    private static Task<HttpResponseMessage> GetSecureAsync(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostLogoutAsync(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    /// <summary>
    /// Self-contained Kestrel host wiring the real production pieces this ticket touches: the same
    /// <see cref="SupabaseJwtBearerConfiguration"/> HR.Api's Program.cs configures (E2E/local-key
    /// mode, so tokens are signed with <see cref="E2eFakeSupabaseJwt.SigningKey"/> rather than
    /// requiring a live Supabase JWKS endpoint), a real <see cref="IdentityDbContext"/> against the
    /// Postgres testcontainer, the real <see cref="SessionRevocationStore"/>, and a
    /// "/api/logout" endpoint that mirrors the production
    /// <c>HR.Modules.Identity.Features.Logout.Endpoint</c>'s own "sub"-from-ClaimsPrincipal +
    /// LogoutHandler wiring exactly (same production <c>LogoutHandler</c> class, same claim source).
    /// "/api/me" stands in for HR.Api's real simple authenticated endpoint used elsewhere in this
    /// suite for authenticated-endpoint smoke tests.
    /// </summary>
    private sealed class RevocationTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private RevocationTestHost(WebApplication app) => _app = app;

        public static async Task<RevocationTestHost> StartAsync(string connectionString)
        {
            var settings = new Dictionary<string, string?>
            {
                ["SupabaseAuth:ProjectUrl"] = SupabaseProjectUrl,
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(o =>
                    SupabaseJwtBearerConfiguration.ConfigureValidation(o, config, isE2ETesting: true));

            builder.Services
                .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<ILoggerFactory>((o, lf) =>
                    SupabaseJwtBearerConfiguration.AttachConfigurationManager(
                        o, config, lf, isE2ETesting: true));

            builder.Services.AddAuthorization();

            builder.Services.AddDbContext<IdentityDbContext>(o =>
                o.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity")));
            builder.Services.AddScoped<ISessionRevocationStore, SessionRevocationStore>();
            builder.Services.AddScoped<ISupabaseAuthGateway, NoOpSupabaseAuthGateway>();
            builder.Services.AddSingleton<IClock, SystemClock>();
            builder.Services.AddScoped<HR.Modules.Identity.Features.Logout.LogoutHandler>();

            var app = builder.Build();

            // Migrate the identity schema once, before serving any request.
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS identity");
                await db.Database.MigrateAsync();
            }

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapGet("/api/me", () => Results.Ok(new { ok = true })).RequireAuthorization();

            // Mirrors HR.Modules.Identity.Features.Logout.Endpoint's own bearer/"sub" extraction and
            // LogoutHandler call verbatim (see that class's remarks): ASP.NET Core's authentication
            // middleware always runs and populates HttpContext.User for a genuinely valid bearer, even
            // though this endpoint itself is anonymous.
            app.MapPost("/api/logout", async (HttpContext httpContext, HR.Modules.Identity.Features.Logout.LogoutHandler handler) =>
            {
                string? accessToken = null;
                if (AuthenticationHeaderValue.TryParse(
                        httpContext.Request.Headers.Authorization.ToString(), out var parsed)
                    && string.Equals(parsed.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
                {
                    accessToken = parsed.Parameter;
                }

                Guid? supabaseAuthUserId = null;
                if (httpContext.User.Identity?.IsAuthenticated == true
                    && Guid.TryParse(httpContext.User.FindFirst("sub")?.Value, out var parsedUserId))
                {
                    supabaseAuthUserId = parsedUserId;
                }

                await handler.HandleAsync(accessToken, supabaseAuthUserId, httpContext.RequestAborted);
                return Results.Ok();
            }).AllowAnonymous();

            await app.StartAsync();
            return new RevocationTestHost(app);
        }

        public HttpClient CreateClient() => new() { BaseAddress = new Uri(_app.Urls.First()) };

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }

    /// <summary>No-op Supabase gateway: the real upstream sign-out call is irrelevant to this ticket
    /// (see LogoutHandlerTests.Records_Revocation_Even_When_Upstream_Supabase_Sign_Out_Fails for the
    /// "Supabase call independence" behaviour, already covered at the unit level) — this host only
    /// needs SignOutAsync to not throw so LogoutHandler's happy path completes.</summary>
    private sealed class NoOpSupabaseAuthGateway : ISupabaseAuthGateway
    {
        public Task<Guid> CreateUserAsync(string email, string password, string redirectTo, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ResendVerificationEmailAsync(string email, string redirectTo, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task RequestPasswordResetAsync(string email, string redirectTo, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<string> GenerateRecoveryLinkAsync(string email, string redirectTo, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UpdatePasswordAsync(string userAccessToken, string newPassword, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<SupabaseSession> ExchangeCodeForSessionAsync(string code, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Guid> EnsureDevUserAsync(string email, string password, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Guid> CreateConfirmedUserAsync(string email, string password, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<SupabaseSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task SignOutAsync(string userAccessToken, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<int> RemoveAllMfaFactorsAsync(Guid supabaseUserId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Guid?> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
