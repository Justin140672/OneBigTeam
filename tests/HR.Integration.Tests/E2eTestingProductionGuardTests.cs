using HR.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 1 — the startup guard in HR.Api/Program.cs must refuse to boot when
/// <c>E2E_TESTING=true</c> outside the Development environment, so the test-authentication plumbing
/// (fake Supabase gateway, non-secret JWT key) can never be reached in a real deployment.
///
/// This lives in the "Integration" collection purely so it is serialized against the other
/// integration tests: it toggles the process-wide <c>E2E_TESTING</c> environment variable, which
/// must not race a parallel host build. It does not use the shared <c>ApiWebApplicationFactory</c>
/// fixture.
/// </summary>
[Collection("Integration")]
public class E2eTestingProductionGuardTests
{
    private sealed class ProductionApiFactory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Production);
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment(Environments.Production);
    }

    [Fact]
    public void Host_Refuses_To_Start_When_E2E_TESTING_Is_True_And_Environment_Is_Production()
    {
        var originalE2E = Environment.GetEnvironmentVariable("E2E_TESTING");
        var originalConn = Environment.GetEnvironmentVariable("ConnectionStrings__hr");

        Environment.SetEnvironmentVariable("E2E_TESTING", "true");
        // The guard runs after the connection string is read but before any DB access — give it a
        // syntactically-valid dummy so we hit the guard, not the "connection string not found" throw.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__hr",
            "Host=localhost;Port=5432;Database=guard_test;Username=postgres;Password=postgres");

        try
        {
            using var factory = new ProductionApiFactory();

            var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

            Assert.Contains("E2E_TESTING", Flatten(exception));
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", originalE2E);
            Environment.SetEnvironmentVariable("ConnectionStrings__hr", originalConn);
        }
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
            messages.Add(current.Message);

        if (exception is AggregateException aggregate)
            messages.AddRange(aggregate.Flatten().InnerExceptions.Select(e => e.Message));

        return string.Join(" | ", messages);
    }
}
