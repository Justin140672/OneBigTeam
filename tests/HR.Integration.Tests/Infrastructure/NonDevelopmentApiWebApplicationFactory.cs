using HR.Modules.Companies.Services;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Standalone (non-shared, not collection-fixtured) variant of <see cref="ApiWebApplicationFactory"/>
/// that boots the host under the "Production" environment instead of the default "Development" one
/// WebApplicationFactory uses. Exists solely so DevActivateCompanyEndpointTests can assert that
/// /api/dev/* endpoints 404 outside Development, mirroring every other /api/dev/* endpoint's guard.
/// Spins up its own Postgres container per test class instance rather than sharing the collection's
/// fixture, since it can't reuse ApiWebApplicationFactory's shared instance (environment is fixed at
/// host build time).
/// </summary>
public sealed class NonDevelopmentApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("hr_integration_non_dev")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

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
        builder.UseEnvironment("Production");

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

            services.AddSingleton<IEmailSender>(new FakeEmailSender());
            services.AddSingleton<IInviteLinkBuilder, FakeInviteLinkBuilder>();
            services.AddScoped<IStripeGateway>(_ => new FakeStripeGateway());
            services.AddScoped<ISupabaseAuthGateway>(_ => new FakeSupabaseAuthGateway());
        });
    }
}
