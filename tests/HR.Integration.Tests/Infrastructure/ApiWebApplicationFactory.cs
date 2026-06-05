using Xunit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HR.Integration.Tests.Infrastructure;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string? _connectionString;
    private bool _isStarted;
    private readonly SemaphoreSlim _startupLock = new(1, 1);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("hr_integration")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    async Task IAsyncLifetime.InitializeAsync()
    {
        await EnsurePostgresStartedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_isStarted)
        {
            await _postgres.DisposeAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        EnsurePostgresStartedAsync().GetAwaiter().GetResult();
        builder.UseSetting("ConnectionStrings:hr", _connectionString);

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
        });
    }

    private async Task EnsurePostgresStartedAsync()
    {
        if (_isStarted)
        {
            return;
        }

        await _startupLock.WaitAsync();
        try
        {
            if (_isStarted)
            {
                return;
            }

            await _postgres.StartAsync();
            _connectionString = _postgres.GetConnectionString();
            _isStarted = true;
        }
        finally
        {
            _startupLock.Release();
        }
    }
}
