using System.Net;
using HR.Modules.Companies.Services;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Config-only variant of <see cref="ApiWebApplicationFactory"/>, mirroring
/// <see cref="ContactApiWebApplicationFactory"/>'s pattern, used only by
/// <c>IdentityRateLimitingTests</c> to exercise the actual 429/threshold/window-recovery behaviour
/// of the identity rate limiters (P1 "Add abuse protection to public identity endpoints").
///
/// The shared <see cref="ApiWebApplicationFactory"/> deliberately widens
/// Identity:RateLimits:*:PermitLimit to 1000 so the rest of the integration suite never trips the
/// limiter; this factory does the opposite — it narrows the identity-forgot-password policy to a
/// small, deterministic PermitLimit=3 over a WindowSeconds=30 (see the WindowSeconds override
/// added to IdentityRateLimiting.AddPolicy) so tests can reliably exceed it, observe recovery, and
/// assert partitioning, without a multi-minute real-time wait per test.
///
/// Only identity-forgot-password is narrowed (chosen because RequestPasswordResetHandler always
/// returns success without calling any external gateway when the email has no matching profile —
/// cheapest of the six endpoints to call repeatedly with no test-double setup required). The other
/// five policies are left at their production defaults; no test in this class calls them.
///
/// Like ContactApiWebApplicationFactory, this does not start its own Postgres container — it relies
/// on being part of the "Integration" xUnit collection, so ApiWebApplicationFactory's shared,
/// already-migrated container is up before this factory builds its host.
///
/// Also registers <see cref="TestClientIpStartupFilter"/>, a test-only middleware (added as the
/// outermost pipeline layer via IStartupFilter, ahead of Program.cs's own UseForwardedHeaders/
/// UseRouting/UseRateLimiter) that lets tests set HttpContext.Connection.RemoteIpAddress directly
/// from an "X-Test-Remote-Ip" request header. This is necessary because the in-process TestServer
/// transport used by WebApplicationFactory never populates a real RemoteIpAddress, so without this
/// hook every request in-process would collapse onto the same "unknown-ip" rate-limit partition
/// component and different-IP-partition / forwarded-header-spoofing tests would be unwritable. This
/// is purely additive test infrastructure — it does not change any production pipeline code, and it
/// runs BEFORE the production ForwardedHeadersOptions check, so that check still exercises its real,
/// unmodified trust logic against a realistic (test-supplied) "real" connection IP.
/// </summary>
public sealed class IdentityRateLimitApiWebApplicationFactory : WebApplicationFactory<Program>
{
    public const int PermitLimit = 3;
    public const int WindowSeconds = 30;

    public FakeEmailSender EmailSender { get; } = new FakeEmailSender();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // See ContactApiWebApplicationFactory for why these are needed: this factory does
                // not derive from ApiWebApplicationFactory, so it does not inherit the sensitive-data
                // encryption keys that factory injects, which are required for EmployeesDbContext to
                // construct successfully during startup migrations.
                ["Infrastructure:SensitiveDataProtection:ActiveKeyId"] = "test",
                ["Infrastructure:SensitiveDataProtection:Keys:test"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                ["Identity:RateLimits:identity-forgot-password:PermitLimit"] = PermitLimit.ToString(),
                ["Identity:RateLimits:identity-forgot-password:WindowSeconds"] = WindowSeconds.ToString(),
                // Deliberately NOT setting Identity:TrustedProxies/TrustedProxyNetworks here — the
                // forwarded-header spoofing-resistance test relies on them being empty (the
                // production default) so X-Forwarded-For is ignored for a connection IP that isn't a
                // configured trusted proxy.
            });
        });

        builder.ConfigureServices(services =>
        {
            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName,
                    _ =>
                    {
                    });

            services.AddSingleton<IEmailSender>(EmailSender);
            services.AddSingleton<IInviteLinkBuilder, FakeInviteLinkBuilder>();
            services.AddScoped<IStripeGateway>(_ => new FakeStripeGateway());
            services.AddScoped<ISupabaseAuthGateway>(_ => new FakeSupabaseAuthGateway());

            services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, TestClientIpStartupFilter>();
        });
    }
}

/// <summary>
/// Test-only IStartupFilter (see IdentityRateLimitApiWebApplicationFactory's remarks) that sets
/// HttpContext.Connection.RemoteIpAddress from an "X-Test-Remote-Ip" request header, when present,
/// before any of Program.cs's own middleware runs. Never registered outside this test factory.
/// </summary>
public sealed class TestClientIpStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
{
    public const string HeaderName = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out var value)
                && IPAddress.TryParse(value.ToString(), out var ip))
            {
                context.Connection.RemoteIpAddress = ip;
            }

            await nextMiddleware();
        });

        next(app);
    };
}
