using HR.Modules.Companies.Services;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Config-only variant of <see cref="ApiWebApplicationFactory"/> used by ContactEndpointTests.
/// The shared factory boots with the default appsettings.json where
/// Marketing:ContactForm:RecipientEmail is intentionally blank (see the 503 test); this factory
/// overrides that setting so the happy-path/validation/honeypot tests can exercise the
/// "recipient is configured" branch, and keeps its own <see cref="FakeEmailSender"/> so its
/// Assert.Single/Assert.Empty email assertions stay isolated from the rest of the suite.
///
/// It no longer starts its own Postgres container: ContactEndpointTests is part of the
/// "Integration" collection, so <see cref="ApiWebApplicationFactory"/>'s shared, already-migrated
/// container is up (and its connection string exported to ConnectionStrings__hr) before this
/// factory builds its host. The contact form endpoint writes no tenant data, so sharing that
/// database is safe.
/// </summary>
public sealed class ContactApiWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string RecipientEmail = "contact-test-recipient@example.com";

    public FakeEmailSender EmailSender { get; } = new FakeEmailSender();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Marketing:ContactForm:RecipientEmail"] = RecipientEmail,
                // This factory does not derive from ApiWebApplicationFactory, so it does not inherit
                // the sensitive-data encryption keys that factory injects. Without them the
                // ISensitiveDataProtector singleton throws on first resolution — which happens when
                // EmployeesDbContext is constructed during startup migrations, leaving the API in
                // "health endpoints only" mode and every /api/contact request answered with 404.
                // A fixed throwaway AES-256 key (32 zero bytes, base64), matching ApiWebApplicationFactory.
                ["Infrastructure:SensitiveDataProtection:ActiveKeyId"] = "test",
                ["Infrastructure:SensitiveDataProtection:Keys:test"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                // This test class makes many requests against one in-memory TestServer, all sharing
                // a single per-IP partition on Program.cs's real "contact-form" rate limiter (5 / 5
                // min in production) — widen it here so validation/happy-path tests don't trip it.
                // No test in this class exercises the limiter itself, so this loses no coverage.
                ["Marketing:ContactForm:RateLimit:PermitLimit"] = "1000",
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
        });
    }
}
