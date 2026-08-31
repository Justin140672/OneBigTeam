using Hangfire;
using HR.Modules.Companies.Services;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HR.Integration.Tests.Infrastructure;

public class ApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("hr_integration")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public FakeEmailSender EmailSender { get; } = new FakeEmailSender();

    internal FakeStripeGateway StripeGateway { get; } = new FakeStripeGateway();

    internal FakeSupabaseAuthGateway SupabaseAuthGateway { get; } = new FakeSupabaseAuthGateway();

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

            // Replace real email sender and link builder with test doubles
            services.AddSingleton<IEmailSender>(EmailSender);
            services.AddSingleton<IInviteLinkBuilder, FakeInviteLinkBuilder>();

            // Replace the real Stripe gateway so no test ever calls out to Stripe's network API.
            services.AddScoped<IStripeGateway>(_ => StripeGateway);

            // Replace the real Supabase Auth gateway so no test ever calls out to Supabase's live
            // Auth Admin API.
            services.AddScoped<ISupabaseAuthGateway>(_ => SupabaseAuthGateway);

            // Replace the real Hangfire-backed IBackgroundJobClient with a no-op fake. Registered
            // after AddHangfireBackgroundJobs (Program.cs) has already wired up the real Hangfire
            // server/storage against the Postgres testcontainer, so this override wins for the
            // IBackgroundJobClient interface while leaving the Hangfire server/dashboard/health
            // check plumbing itself intact. See FakeBackgroundJobClient for why this matters: real
            // job execution otherwise races test-driven state (e.g. ScanUploadedFileJob vs a
            // test's manual "mark scan clean" step).
            services.AddSingleton<IBackgroundJobClient, FakeBackgroundJobClient>();
        });
    }
}
