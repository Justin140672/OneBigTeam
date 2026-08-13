using HR.Modules.Companies.Services;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Standalone (non-shared, not collection-fixtured) variant of <see cref="ApiWebApplicationFactory"/>
/// used solely by ContactEndpointTests. The shared <see cref="ApiWebApplicationFactory"/> boots with
/// the default appsettings.json, where Marketing:ContactForm:RecipientEmail is intentionally blank
/// (see 503 test) — this factory overrides that setting to a configured test recipient so the
/// happy-path/validation/honeypot tests can exercise the "recipient is configured" branch. Spins up
/// its own Postgres container per test class instance for the same reason
/// NonDevelopmentApiWebApplicationFactory does: it can't reuse ApiWebApplicationFactory's shared
/// instance, since configuration is fixed at host build time.
/// </summary>
public sealed class ContactApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string RecipientEmail = "contact-test-recipient@example.com";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("hr_integration_contact")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public FakeEmailSender EmailSender { get; } = new FakeEmailSender();

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__hr", _postgres.GetConnectionString());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__hr", null);
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Marketing:ContactForm:RecipientEmail"] = RecipientEmail,
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
