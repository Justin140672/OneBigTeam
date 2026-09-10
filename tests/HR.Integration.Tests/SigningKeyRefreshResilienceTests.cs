using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

using HR.Api.Authentication;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 6 — resilient Supabase signing-key refresh. Exercises the real
/// <see cref="SupabaseJwtBearerConfiguration"/> wiring (async <c>ConfigurationManager</c> +
/// <c>SupabaseJwksRetriever</c>) against a fully controlled in-process JWKS server.
///
/// This suite deliberately does NOT use <c>ApiWebApplicationFactory</c>: that factory installs
/// <c>TestAuthHandler</c> which short-circuits JwtBearer entirely. Each test builds its own tiny
/// Kestrel host with the production JWT bearer pipeline and a controlled key server, both on
/// dynamic ports, so signature/issuer/audience/algorithm/refresh behaviour is genuinely under test.
/// </summary>
public sealed class SigningKeyRefreshResilienceTests
{
    private const string Issuer = "https://test-project.supabase.co/auth/v1";
    private const string Audience = "authenticated";

    // ---------------------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Valid_rs256_token_signed_by_a_published_key_is_accepted()
    {
        var key = TestKey.NewRsa("rsa-1");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(keyServer.JwksUrl);

        var response = await host.GetSecureAsync(Mint(key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Valid_es256_token_signed_by_a_published_key_is_accepted()
    {
        var key = TestKey.NewEc("ec-1");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(keyServer.JwksUrl);

        var response = await host.GetSecureAsync(Mint(key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------------
    // 2. Anonymous
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Secure_endpoint_without_a_token_is_401_while_anonymous_endpoint_is_200()
    {
        var key = TestKey.NewRsa("rsa-1");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(keyServer.JwksUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/anon")).StatusCode);
    }

    // ---------------------------------------------------------------------
    // 3-7. Token validation failures
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Token_signed_with_a_key_that_is_not_published_is_rejected()
    {
        var published = TestKey.NewRsa("rsa-1");
        await using var keyServer = await ControlledKeyServer.StartAsync(published);
        await using var host = await AuthTestHost.StartAsync(keyServer.JwksUrl);

        // kid matches the published key, but the signing material does not.
        var forged = Mint(published, overrideCredentials: RsaCredentialsWithKid("rsa-1"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(forged)).StatusCode);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var key = TestKey.NewRsa("rsa-1");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(keyServer.JwksUrl);

        var token = Mint(key, expires: DateTime.UtcNow.AddMinutes(-5));

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Token_with_the_wrong_issuer_is_rejected()
    {
        var key = TestKey.NewRsa("rsa-1");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(keyServer.JwksUrl);

        var token = Mint(key, issuer: "https://attacker.example/auth/v1");

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Token_with_the_wrong_audience_is_rejected()
    {
        var key = TestKey.NewRsa("rsa-1");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(keyServer.JwksUrl);

        var token = Mint(key, audience: "service_role");

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Token_signed_with_a_disallowed_algorithm_is_rejected()
    {
        var key = TestKey.NewRsa("rsa-1");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(keyServer.JwksUrl);

        // HS256 is never in the real-path ValidAlgorithms (ES256/RS256 only).
        var token = Mint(key, overrideCredentials: Hs256CredentialsWithKid("rsa-1"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(token)).StatusCode);
    }

    // ---------------------------------------------------------------------
    // 8. Unknown kid -> exactly one bounded refresh + retry, storm-capped
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Unknown_kid_triggers_one_bounded_refresh_then_repeated_unknown_kids_are_throttled()
    {
        var keyA = TestKey.NewRsa("A");
        var keyB = TestKey.NewRsa("B");
        await using var keyServer = await ControlledKeyServer.StartAsync(keyA);
        await using var host = await AuthTestHost.StartAsync(keyServer.JwksUrl);

        // Cold fetch: exactly one upstream hit.
        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(keyA))).StatusCode);
        Assert.Equal(1, keyServer.Hits);

        // Rotate B in as the active key, present a B token: one refresh, then 200. The delay clears
        // the inner manager's 1s real-time fetch floor so the single forced refresh actually runs.
        keyServer.Publish(keyB);
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        Assert.Equal(HttpStatusCode.OK, await EventuallyStatusAsync(host, keyB, HttpStatusCode.OK));

        // Refresh storm: many distinct unknown kids inside the RefreshInterval window.
        var hitsBeforeStorm = keyServer.Hits;
        for (var i = 0; i < 10; i++)
        {
            var ghost = TestKey.NewRsa($"ghost-{i}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(Mint(ghost))).StatusCode);
        }

        Assert.True(
            keyServer.Hits - hitsBeforeStorm <= 1,
            $"refresh storm should add at most one upstream fetch, added {keyServer.Hits - hitsBeforeStorm}");
    }

    // ---------------------------------------------------------------------
    // 9. Cold start, upstream down -> fail closed, promptly
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Cold_start_with_upstream_returning_503_fails_closed_without_hanging()
    {
        var key = TestKey.NewRsa("A");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        keyServer.GoDown();
        await using var host = await AuthTestHost.StartAsync(
            keyServer.JwksUrl, keyFetchTimeout: TimeSpan.FromSeconds(2));

        var stopwatch = Stopwatch.StartNew();
        var response = await host.GetSecureAsync(Mint(key));
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"took {stopwatch.Elapsed}");
    }

    // ---------------------------------------------------------------------
    // 10. Key rotation with standby, no host restart
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Key_rotation_with_a_standby_key_is_picked_up_without_restarting_the_host()
    {
        var keyA = TestKey.NewRsa("A");
        var keyB = TestKey.NewRsa("B");
        await using var keyServer = await ControlledKeyServer.StartAsync(keyA);
        await using var host = await AuthTestHost.StartAsync(keyServer.JwksUrl);

        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(keyA))).StatusCode);

        // Standby: both keys published, tokens still signed by A.
        keyServer.Publish(keyA, keyB);
        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(keyA))).StatusCode);

        // A retired, B now active; first B token is an unknown-kid -> one refresh -> accepted. The
        // delay clears the inner manager's 1s real-time fetch floor.
        keyServer.Publish(keyB);
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        // The unknown kid triggers a background refresh; IdentityModel does not guarantee it lands on
        // the triggering request, so a subsequent request must succeed with no restart.
        Assert.Equal(HttpStatusCode.OK, await EventuallyStatusAsync(host, keyB, HttpStatusCode.OK));
    }

    // ---------------------------------------------------------------------
    // 11. Malformed JWKS -> keep serving last-known-good, warn
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Malformed_jwks_document_keeps_last_known_good_keys_and_logs_a_warning()
    {
        var keyA = TestKey.NewRsa("A");
        await using var keyServer = await ControlledKeyServer.StartAsync(keyA);
        await using var host = await AuthTestHost.StartAsync(keyServer.JwksUrl);

        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(keyA))).StatusCode);

        keyServer.GoMalformed();
        // Inner ConfigurationManager keeps a 1s real-time floor between fetches.
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        // Unknown kid forces the (first, un-throttled) refresh, which fails against the malformed
        // document.
        var ghost = TestKey.NewRsa("ghost");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(Mint(ghost))).StatusCode);

        // The good key A still validates from the retained last-known-good configuration.
        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(keyA))).StatusCode);

        Assert.Contains(
            host.Logs.Messages,
            m => m.Contains("retrieval failed", StringComparison.OrdinalIgnoreCase)
                 && m.Contains("Exception", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------------
    // 12. Upstream slow -> requests not blocked, LKG used, pool not starved
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Slow_upstream_does_not_block_concurrent_requests_that_can_use_cached_keys()
    {
        var keyA = TestKey.NewRsa("A");
        await using var keyServer = await ControlledKeyServer.StartAsync(keyA);
        await using var host = await AuthTestHost.StartAsync(
            keyServer.JwksUrl, keyFetchTimeout: TimeSpan.FromSeconds(1));

        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(keyA))).StatusCode);

        keyServer.GoSlow(TimeSpan.FromSeconds(4));

        var token = Mint(keyA);
        var stopwatch = Stopwatch.StartNew();
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => host.GetSecureAsync(token)));
        stopwatch.Stop();

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"20 requests took {stopwatch.Elapsed}");
    }

    // ---------------------------------------------------------------------
    // 13. Outage beyond MaximumCachedKeyAge -> fail closed, promptly, no hang
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Outage_past_maximum_cached_key_age_fails_closed_promptly()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var keyA = TestKey.NewRsa("A");
        await using var keyServer = await ControlledKeyServer.StartAsync(keyA);
        await using var host = await AuthTestHost.StartAsync(
            keyServer.JwksUrl,
            keyFetchTimeout: TimeSpan.FromSeconds(1),
            maximumCachedKeyAge: TimeSpan.FromHours(1),
            timeProvider: clock);

        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(keyA))).StatusCode);
        await Task.Delay(500); // anchor the freshness timestamp

        keyServer.GoDown();
        clock.Advance(TimeSpan.FromMinutes(61)); // past MaximumCachedKeyAge

        var stopwatch = Stopwatch.StartNew();
        var response = await host.GetSecureAsync(Mint(keyA));
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), $"took {stopwatch.Elapsed}");

        // Repeat call also fails closed and does not hang.
        var second = Stopwatch.StartNew();
        var response2 = await host.GetSecureAsync(Mint(keyA));
        second.Stop();
        Assert.Equal(HttpStatusCode.Unauthorized, response2.StatusCode);
        Assert.True(second.Elapsed < TimeSpan.FromSeconds(8), $"took {second.Elapsed}");
    }

    // ---------------------------------------------------------------------
    // 14. Concurrent first requests -> single upstream fetch (single-flight)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Concurrent_cold_requests_result_in_a_single_upstream_key_fetch()
    {
        var key = TestKey.NewRsa("A");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(keyServer.JwksUrl);

        var token = Mint(key);
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 15).Select(_ => host.GetSecureAsync(token)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, keyServer.Hits);
    }

    // ---------------------------------------------------------------------
    // 15. MaximumCachedKeyAge enforcement (injected clock, real bearer pipeline)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Just_before_the_max_age_boundary_the_cached_keys_still_authenticate()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var key = TestKey.NewRsa("A");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(
            keyServer.JwksUrl, maximumCachedKeyAge: TimeSpan.FromHours(10), timeProvider: clock);

        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(key))).StatusCode);
        await Task.Delay(500); // anchor the freshness timestamp before advancing the simulated clock

        keyServer.GoDown();
        clock.Advance(TimeSpan.FromHours(10) - TimeSpan.FromMinutes(1));

        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(key))).StatusCode);
    }

    [Fact]
    public async Task At_the_max_age_boundary_the_cached_keys_can_no_longer_authenticate()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var key = TestKey.NewRsa("A");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(
            keyServer.JwksUrl, maximumCachedKeyAge: TimeSpan.FromHours(10), timeProvider: clock);

        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(key))).StatusCode);
        await Task.Delay(500); // anchor the freshness timestamp before advancing the simulated clock

        keyServer.GoDown();
        clock.Advance(TimeSpan.FromHours(10));

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(Mint(key))).StatusCode);
    }

    [Fact]
    public async Task Successful_token_validations_do_not_renew_signing_key_freshness()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var key = TestKey.NewRsa("A");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(
            keyServer.JwksUrl, maximumCachedKeyAge: TimeSpan.FromHours(10), timeProvider: clock);

        // Warm the cache, then let any secondary cold-start fetch land before the simulated clock
        // starts moving so the freshness timestamp is firmly anchored at "now".
        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(key))).StatusCode);
        await Task.Delay(500);
        keyServer.GoDown();

        // Nine hours of steady, successful traffic - one validation per simulated hour.
        for (var hour = 0; hour < 9; hour++)
        {
            Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(key))).StatusCode);
            clock.Advance(TimeSpan.FromHours(1));
        }

        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1)); // now 10h1m since the only fetch

        // If validations had renewed freshness this would still be 200.
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(Mint(key))).StatusCode);
    }

    [Fact]
    public async Task Repeated_failed_refreshes_during_an_outage_do_not_extend_the_cached_key_age()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var key = TestKey.NewRsa("A");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(
            keyServer.JwksUrl,
            keyFetchTimeout: TimeSpan.FromSeconds(1),
            maximumCachedKeyAge: TimeSpan.FromHours(10),
            timeProvider: clock);

        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(key))).StatusCode);
        await Task.Delay(500); // anchor the freshness timestamp

        keyServer.GoDown();

        // Drive a failed forced refresh each simulated hour for 9 hours.
        for (var hour = 0; hour < 9; hour++)
        {
            clock.Advance(TimeSpan.FromHours(1));
            var ghost = TestKey.NewRsa($"ghost-{hour}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(Mint(ghost))).StatusCode);
        }

        // 9h in, still inside the 10h window: the original cached key authenticates.
        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(key))).StatusCode);

        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));

        // The failed refreshes did not move the freshness timestamp - now closed.
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(Mint(key))).StatusCode);
    }

    [Fact]
    public async Task A_later_successful_retrieval_restores_authentication_without_a_restart()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var key = TestKey.NewRsa("A");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(
            keyServer.JwksUrl, maximumCachedKeyAge: TimeSpan.FromHours(10), timeProvider: clock);

        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(key))).StatusCode);
        await Task.Delay(500); // anchor the freshness timestamp

        keyServer.GoDown();
        clock.Advance(TimeSpan.FromHours(11));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(Mint(key))).StatusCode);

        // Upstream recovers. Allow the inner manager's 1s real-time fetch floor to pass.
        keyServer.Publish(key);
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        // The refresh triggered here may or may not satisfy the same request; a subsequent request
        // must succeed with no restart.
        await host.GetSecureAsync(Mint(key));
        var eventual = await host.GetSecureAsync(Mint(key));
        Assert.Equal(HttpStatusCode.OK, eventual.StatusCode);
    }

    [Fact]
    public async Task Re_fetching_the_same_key_set_renews_freshness()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var key = TestKey.NewRsa("A");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(
            keyServer.JwksUrl, maximumCachedKeyAge: TimeSpan.FromHours(10), timeProvider: clock);

        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(key))).StatusCode);
        var hitsAfterColdStart = keyServer.Hits;

        // 9h later, still serving the identical key set. An unknown kid forces a background refresh.
        clock.Advance(TimeSpan.FromHours(9));
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        var ghost = TestKey.NewRsa("ghost");
        for (var i = 0; i < 20 && keyServer.Hits <= hitsAfterColdStart; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(Mint(ghost))).StatusCode);
            await Task.Delay(150);
        }

        Assert.True(keyServer.Hits > hitsAfterColdStart, "the forced refresh should have re-fetched");
        await Task.Delay(300); // let the re-fetch publish its freshness snapshot

        // 5h after that re-fetch (14h after cold start). Without renewal this would be past the 10h
        // limit; because the re-fetch renewed freshness, the key still authenticates.
        clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(key))).StatusCode);
    }

    [Fact]
    public async Task Forged_signature_is_rejected_even_while_cached_keys_are_within_max_age()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var key = TestKey.NewRsa("A");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(
            keyServer.JwksUrl, maximumCachedKeyAge: TimeSpan.FromHours(10), timeProvider: clock);

        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(key))).StatusCode);
        await Task.Delay(500); // anchor the freshness timestamp
        keyServer.GoDown();
        clock.Advance(TimeSpan.FromHours(5));

        var forged = Mint(key, overrideCredentials: RsaCredentialsWithKid("A"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetSecureAsync(forged)).StatusCode);
        // Genuine token from the same cache still works.
        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(Mint(key))).StatusCode);
    }

    // ---------------------------------------------------------------------
    // 16. E2E local-key path never touches the JWKS network
    // ---------------------------------------------------------------------

    [Fact]
    public async Task E2E_local_key_path_authenticates_without_any_jwks_fetch()
    {
        var key = TestKey.NewRsa("A");
        await using var keyServer = await ControlledKeyServer.StartAsync(key);
        await using var host = await AuthTestHost.StartAsync(keyServer.JwksUrl, isE2ETesting: true);

        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                HR.Modules.Identity.Services.E2eFakeSupabaseJwt.SigningKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString() },
        });

        Assert.Equal(HttpStatusCode.OK, (await host.GetSecureAsync(token)).StatusCode);
        Assert.Equal(0, keyServer.Hits);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>Minimal manual fake clock - the gate only ever calls <see cref="GetUtcNow"/>.</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private long _ticks;

        public FakeTimeProvider(DateTimeOffset start) => _ticks = start.UtcTicks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }

    /// <summary>
    /// Polls the secure endpoint until it returns <paramref name="expected"/> or the attempts run out.
    /// IdentityModel refreshes signing keys on a background task, so the request that triggers a
    /// refresh is not guaranteed to see its result.
    /// </summary>
    private static async Task<HttpStatusCode> EventuallyStatusAsync(
        AuthTestHost host, TestKey key, HttpStatusCode expected)
    {
        var status = HttpStatusCode.InternalServerError;
        for (var attempt = 0; attempt < 25; attempt++)
        {
            status = (await host.GetSecureAsync(Mint(key))).StatusCode;
            if (status == expected)
            {
                return status;
            }

            await Task.Delay(150);
        }

        return status;
    }

    private static string Mint(
        TestKey key,
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expires = null,
        SigningCredentials? overrideCredentials = null)
    {
        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = DateTime.UtcNow.AddMinutes(-2),
            NotBefore = DateTime.UtcNow.AddMinutes(-2),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = overrideCredentials ?? key.Credentials,
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString() },
        };
        return handler.CreateToken(descriptor);
    }

    private static SigningCredentials RsaCredentialsWithKid(string kid)
        => new(new RsaSecurityKey(RSA.Create(2048)) { KeyId = kid }, SecurityAlgorithms.RsaSha256);

    private static SigningCredentials Hs256CredentialsWithKid(string kid)
        => new(
            new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = kid },
            SecurityAlgorithms.HmacSha256);

    /// <summary>An in-test signing key plus its public JWK representation.</summary>
    private sealed class TestKey
    {
        public required string Kid { get; init; }

        public required SigningCredentials Credentials { get; init; }

        public required Dictionary<string, string> Jwk { get; init; }

        public static TestKey NewRsa(string kid)
        {
            var rsa = RSA.Create(2048);
            var pub = JsonWebKeyConverter.ConvertFromRSASecurityKey(
                new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: false)));
            return new TestKey
            {
                Kid = kid,
                Credentials = new SigningCredentials(
                    new RsaSecurityKey(rsa) { KeyId = kid }, SecurityAlgorithms.RsaSha256),
                Jwk = new Dictionary<string, string>
                {
                    ["kty"] = "RSA",
                    ["use"] = "sig",
                    ["alg"] = "RS256",
                    ["kid"] = kid,
                    ["n"] = pub.N,
                    ["e"] = pub.E,
                },
            };
        }

        public static TestKey NewEc(string kid)
        {
            var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var pub = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(
                new ECDsaSecurityKey(ECDsa.Create(ecdsa.ExportParameters(includePrivateParameters: false))));
            return new TestKey
            {
                Kid = kid,
                Credentials = new SigningCredentials(
                    new ECDsaSecurityKey(ecdsa) { KeyId = kid }, SecurityAlgorithms.EcdsaSha256),
                Jwk = new Dictionary<string, string>
                {
                    ["kty"] = "EC",
                    ["use"] = "sig",
                    ["alg"] = "ES256",
                    ["kid"] = kid,
                    ["crv"] = "P-256",
                    ["x"] = pub.X,
                    ["y"] = pub.Y,
                },
            };
        }
    }

    /// <summary>
    /// A controllable JWKS endpoint on its own dynamic port. Behaviour is switched between requests
    /// via volatile fields; every hit is counted for single-flight / throttle assertions.
    /// </summary>
    private sealed class ControlledKeyServer : IAsyncDisposable
    {
        private const int ModeNormal = 0;
        private const int ModeDown = 1;
        private const int ModeMalformed = 2;
        private const int ModeSlow = 3;

        private readonly WebApplication _app;
        private int _hits;
        private volatile List<TestKey> _keys = new();
        private volatile int _mode = ModeNormal;
        private volatile int _delayMs;

        private ControlledKeyServer(WebApplication app, string jwksUrl)
        {
            _app = app;
            JwksUrl = jwksUrl;
        }

        public string JwksUrl { get; }

        public int Hits => Volatile.Read(ref _hits);

        public static async Task<ControlledKeyServer> StartAsync(params TestKey[] keys)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();

            var holder = new ControlledKeyServer[1];

            app.MapGet("/jwks.json", async () =>
            {
                var server = holder[0];
                Interlocked.Increment(ref server._hits);
                switch (server._mode)
                {
                    case ModeDown:
                        return Results.StatusCode((int)HttpStatusCode.ServiceUnavailable);
                    case ModeMalformed:
                        return Results.Text("{ not json", "application/json");
                    case ModeSlow:
                        await Task.Delay(server._delayMs);
                        return Results.Text(server.BuildJwks(), "application/json");
                    default:
                        return Results.Text(server.BuildJwks(), "application/json");
                }
            });

            await app.StartAsync();
            var address = app.Urls.First().TrimEnd('/');
            var instance = new ControlledKeyServer(app, address + "/jwks.json") { _keys = keys.ToList() };
            holder[0] = instance;
            return instance;
        }

        public void Publish(params TestKey[] keys)
        {
            _keys = keys.ToList();
            _mode = ModeNormal;
        }

        public void GoDown() => _mode = ModeDown;

        public void GoMalformed() => _mode = ModeMalformed;

        public void GoSlow(TimeSpan delay)
        {
            _delayMs = (int)delay.TotalMilliseconds;
            _mode = ModeSlow;
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();

        private string BuildJwks() => JsonSerializer.Serialize(new { keys = _keys.Select(k => k.Jwk) });
    }

    /// <summary>
    /// Self-contained Kestrel host running the production Supabase JWT bearer pipeline against a
    /// caller-supplied JWKS URL.
    /// </summary>
    private sealed class AuthTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private AuthTestHost(WebApplication app, CapturingLoggerProvider logs)
        {
            _app = app;
            Logs = logs;
            Client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        }

        public HttpClient Client { get; }

        public CapturingLoggerProvider Logs { get; }

        public static async Task<AuthTestHost> StartAsync(
            string jwksUrl,
            TimeSpan? keyFetchTimeout = null,
            TimeSpan? refreshInterval = null,
            TimeSpan? lastKnownGoodLifetime = null,
            TimeSpan? maximumCachedKeyAge = null,
            TimeProvider? timeProvider = null,
            bool isE2ETesting = false)
        {
            var settings = new Dictionary<string, string?>
            {
                ["SupabaseAuth:ProjectUrl"] = "https://test-project.supabase.co",
                ["SupabaseAuth:JwksUrl"] = jwksUrl,
                ["SupabaseAuth:SigningKeyRefresh:RequireHttpsMetadata"] = "false",
                ["SupabaseAuth:SigningKeyRefresh:KeyFetchTimeout"] =
                    (keyFetchTimeout ?? TimeSpan.FromSeconds(2)).ToString(),
                // Kept at/above the enforced 30s minimum; timing-sensitive tests inject a fake clock
                // rather than shortening this.
                ["SupabaseAuth:SigningKeyRefresh:RefreshInterval"] =
                    (refreshInterval ?? TimeSpan.FromSeconds(30)).ToString(),
                ["SupabaseAuth:SigningKeyRefresh:AutomaticRefreshInterval"] = "00:15:00",
                ["SupabaseAuth:SigningKeyRefresh:LastKnownGoodLifetime"] =
                    (lastKnownGoodLifetime ?? TimeSpan.FromHours(1)).ToString(),
                ["SupabaseAuth:SigningKeyRefresh:MaximumCachedKeyAge"] =
                    (maximumCachedKeyAge ?? TimeSpan.FromHours(24)).ToString(),
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            var logs = new CapturingLoggerProvider();
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(logs);
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(o =>
                    SupabaseJwtBearerConfiguration.ConfigureValidation(o, config, isE2ETesting));

            builder.Services
                .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<ILoggerFactory>((o, lf) =>
                    SupabaseJwtBearerConfiguration.AttachConfigurationManager(
                        o, config, lf, isE2ETesting, timeProvider));

            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGet("/secure", () => "ok").RequireAuthorization();
            app.MapGet("/anon", () => "ok").AllowAnonymous();

            await app.StartAsync();
            return new AuthTestHost(app, logs);
        }

        public Task<HttpResponseMessage> GetSecureAsync(string? token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/secure");
            if (token is not null)
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            }

            return Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }

    /// <summary>Captures formatted log messages (message + exception type only) for assertions.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly CapturingLoggerProvider _provider;
            private readonly string _category;

            public CapturingLogger(CapturingLoggerProvider provider, string category)
            {
                _provider = provider;
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var message = formatter(state, exception);
                if (exception is not null)
                {
                    message += " | ex:" + exception.GetType().Name;
                }

                _provider.Messages.Enqueue($"{_category} {logLevel} {message}");
            }
        }
    }
}
