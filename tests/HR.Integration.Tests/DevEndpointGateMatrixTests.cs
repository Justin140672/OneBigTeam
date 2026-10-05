using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 2 (P1) — dev/E2E-only routes under /api/dev must be absent (404) outside Development, and
/// inside Development must additionally require their own switch (E2E_TESTING for the E2E endpoints,
/// DevTools:Enabled for the tooling endpoints). Covers the environment x E2E_TESTING matrix plus the
/// authorisation/tenant boundaries of the departure-finaliser E2E endpoint.
///
/// Toggles the process-wide E2E_TESTING variable, so it lives in the serialized "Integration" collection.
/// </summary>
[Collection("Integration")]
public class DevEndpointGateMatrixTests
{
    private static readonly Guid AcmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid BetaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid AcmeHrAdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AcmeEmployeeUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BetaHrAdminId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly ApiWebApplicationFactory _factory;

    public DevEndpointGateMatrixTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    // Production / Staging with E2E_TESTING off: dev routes are not registered by the real API pipeline
    // (same FastEndpoints filter/configurator as HR.Api/Program.cs), so every request 404s. Booting the
    // full API as literal Production/Staging needs real storage/encryption secrets, so the full host
    // uses the non-Development "Test" environment (which takes the identical !IsDevelopment path) and
    // the Production/Staging names are covered against a minimal FastEndpoints host below.
    [Fact]
    public async Task NonDevelopment_E2EOff_DevRoutes_Return_404_Even_For_Authorised_Callers_FullHost()
    {
        using var restore = E2eVariable.Set(null);
        await using var factory = CreateFactory("Test");

        using var anonymous = factory.CreateClient();
        using var hrAdmin = factory.CreateClient();
        hrAdmin.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AcmeHrAdminId.ToString());
        hrAdmin.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());

        var finaliserUrl = FinaliserUrl(AcmeCompanyId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsync(finaliserUrl, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hrAdmin.PostAsync(finaliserUrl, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await anonymous.PostAsJsonAsync("/api/dev/activate-company", new { companyId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await anonymous.PostAsJsonAsync("/api/dev/ensure-employee-login", new
            {
                employeeId = Guid.NewGuid(),
                companyId = Guid.NewGuid(),
                email = "x@example.com",
                firstName = "X",
                lastName = "Y",
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/dev/personas")).StatusCode);

        // Not merely gated at request time: no /api/dev route exists at all in the routing table.
        var devRoutes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .Where(r => r.StartsWith("/api/dev", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(devRoutes);
    }

    // Type-discovery matrix over the production Employees + Identity endpoints using the same predicate
    // HR.Api passes to AddFastEndpoints: every [DevOnlyEndpoint] type (including the three known dev
    // endpoints) is discovered only in Development, so it has no route in Production/Staging.
    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    public void DevOnlyEndpoints_Are_Discovered_Only_In_Development(string environmentName, bool expectDiscovered)
    {
        var assemblies = new[]
        {
            typeof(HR.Modules.Employees.EmployeesModule).Assembly,
            typeof(HR.Modules.Identity.IdentityModule).Assembly,
            typeof(HR.Modules.Companies.CompaniesModule).Assembly,
        };
        var devTypes = assemblies.SelectMany(a => a.GetTypes())
            .Where(t => Attribute.IsDefined(t, typeof(HR.SharedKernel.DevEndpoints.DevOnlyEndpointAttribute), false))
            .ToList();

        Assert.Contains(devTypes, t => t.Name == "DepartureFinaliserTestEndpoint");
        Assert.Contains(devTypes, t => t.Name == "Endpoint" && t.Namespace!.EndsWith("DevExpireCompanyTrial"));
        Assert.Equal(4, devTypes.Count);

        var environment = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = environmentName };
        foreach (var type in devTypes)
        {
            Assert.Equal(expectDiscovered, HR.SharedKernel.DevEndpoints.DevEndpointGate.ShouldDiscover(type, environment));
        }
    }

    // Production / Staging with E2E_TESTING on: the host must refuse to start (Ticket 1 guard preserved).
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NonDevelopment_E2EOn_Host_Refuses_To_Start(string environmentName)
    {
        using var restoreE2E = E2eVariable.Set("true");
        var originalConn = Environment.GetEnvironmentVariable("ConnectionStrings__hr");
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__hr",
            "Host=localhost;Port=5432;Database=guard_test;Username=postgres;Password=postgres");

        try
        {
            using var factory = new StartupOnlyFactory(environmentName);
            var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

            var messages = new List<string>();
            for (Exception? current = exception; current is not null; current = current.InnerException)
                messages.Add(current.Message);
            Assert.Contains("E2E_TESTING", string.Join(" | ", messages));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__hr", originalConn);
        }
    }

    // Development with E2E_TESTING off: E2E endpoints are not exposed, even to a correct HR admin,
    // and nothing is mutated.
    [Fact]
    public async Task Development_E2EOff_Finaliser_Returns_404_For_Authorised_HrAdmin_And_Mutates_Nothing()
    {
        await AssignRolesAsync();
        var employeeId = await CreateEmployeeWithInProgressLeavingProcessAsync();

        using var restore = E2eVariable.Set(null);
        using var client = HrAdminClient(AcmeHrAdminId, AcmeCompanyId);

        var response = await client.PostAsync(FinaliserUrl(AcmeCompanyId, employeeId), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(LeavingProcessStatus.InProgress, await GetLeavingProcessStatusAsync(employeeId));
    }

    // Development with E2E_TESTING on: authorised tests keep working.
    [Fact]
    public async Task Development_E2EOn_Anonymous_Returns_401()
    {
        using var restore = E2eVariable.Set("true");
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(FinaliserUrl(AcmeCompanyId, Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Development_E2EOn_Wrong_Role_Returns_403_And_Mutates_Nothing()
    {
        await AssignRolesAsync();
        var employeeId = await CreateEmployeeWithInProgressLeavingProcessAsync();

        using var restore = E2eVariable.Set("true");
        using var client = HrAdminClient(AcmeEmployeeUserId, AcmeCompanyId);

        var response = await client.PostAsync(FinaliserUrl(AcmeCompanyId, employeeId), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(LeavingProcessStatus.InProgress, await GetLeavingProcessStatusAsync(employeeId));
    }

    [Fact]
    public async Task Development_E2EOn_HrAdmin_Of_Other_Tenant_Returns_403_And_Mutates_Nothing()
    {
        await AssignRolesAsync();
        var employeeId = await CreateEmployeeWithInProgressLeavingProcessAsync();

        using var restore = E2eVariable.Set("true");
        using var client = HrAdminClient(BetaHrAdminId, BetaCorpId);

        // Beta admin naming Acme's company in the route: tenant does not match the route company.
        var response = await client.PostAsync(FinaliserUrl(AcmeCompanyId, employeeId), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(LeavingProcessStatus.InProgress, await GetLeavingProcessStatusAsync(employeeId));
    }

    [Fact]
    public async Task Development_E2EOn_HrAdmin_Cannot_Target_Employee_Of_Another_Company()
    {
        await AssignRolesAsync();
        var acmeEmployeeId = await CreateEmployeeWithInProgressLeavingProcessAsync();

        using var restore = E2eVariable.Set("true");
        using var client = HrAdminClient(BetaHrAdminId, BetaCorpId);

        // Route company matches the caller's own tenant (Beta) but the employee belongs to Acme.
        var response = await client.PostAsync(FinaliserUrl(BetaCorpId, acmeEmployeeId), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(LeavingProcessStatus.InProgress, await GetLeavingProcessStatusAsync(acmeEmployeeId));
    }

    [Fact]
    public async Task Development_E2EOn_HrAdmin_Of_Correct_Tenant_Finalises_Successfully()
    {
        await AssignRolesAsync();
        var employeeId = await CreateEmployeeWithInProgressLeavingProcessAsync();
        await MakeLeavingProcessDueAsync(employeeId);

        using var restore = E2eVariable.Set("true");
        using var client = HrAdminClient(AcmeHrAdminId, AcmeCompanyId);

        var response = await client.PostAsync(FinaliserUrl(AcmeCompanyId, employeeId), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LeavingProcessStatus.Completed, await GetLeavingProcessStatusAsync(employeeId));
    }

    // ---- helpers ----

    private static string FinaliserUrl(Guid companyId, Guid employeeId) =>
        $"/api/dev/departure-finaliser/{companyId:N}/{employeeId:N}";

    private HttpClient HrAdminClient(Guid userId, Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        return client;
    }

    private WebApplicationFactory<Program> CreateFactory(string environmentName) =>
        _factory.WithWebHostBuilder(builder => builder.UseEnvironment(environmentName));

    private async Task AssignRolesAsync()
    {
        await TestRoleSeeder.AssignRoleAsync(_factory, AcmeHrAdminId, SystemRoles.HrAdministrator, AcmeCompanyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, AcmeEmployeeUserId, SystemRoles.Employee, AcmeCompanyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, BetaHrAdminId, SystemRoles.HrAdministrator, BetaCorpId,
            ensureActiveSubscription: false);
    }

    private async Task<LeavingProcessStatus> GetLeavingProcessStatusAsync(Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        return await db.EmployeeLeavingProcesses
            .IgnoreQueryFilters()
            .Where(p => p.EmployeeId == employeeId)
            .OrderByDescending(p => p.StartedAt)
            .Select(p => p.Status)
            .FirstAsync();
    }

    private async Task<Guid> CreateEmployeeWithInProgressLeavingProcessAsync()
    {
        using var adminClient = HrAdminClient(AcmeHrAdminId, AcmeCompanyId);

        var unique = Guid.NewGuid().ToString("N")[..12];
        var create = await adminClient.PostAsJsonAsync(
            $"/api/companies/{AcmeCompanyId}/employees",
            new
            {
                firstName = "Gate",
                lastName = $"Employee-{unique}",
                workEmail = $"gate-{unique}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                addressLine1 = "1 Test Street",
                city = "London",
                postCode = "SW1A 1AA",
                gender = "Male",
                employeeNumber = $"GATE-{unique}",
                employmentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                departmentId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                locationId = Guid.Parse("70000000-0000-0000-0000-000000000001"),
                positionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
                companyId = AcmeCompanyId,
            });
        create.EnsureSuccessStatusCode();
        var employeeId = (await create.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        // A future leaving date keeps the process InProgress (not yet due), so a rejected request
        // provably leaves it untouched; MakeLeavingProcessDueAsync backdates it for the success case.
        var leavingDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var leaving = await adminClient.PostAsJsonAsync(
            $"/api/companies/{AcmeCompanyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId = AcmeCompanyId,
                employeeId,
                resignationReceivedDate = DateOnly.FromDateTime(DateTime.UtcNow),
                leavingDate,
                lastWorkingDay = leavingDate,
                leavingReason = "Resignation",
            });
        leaving.EnsureSuccessStatusCode();
        return employeeId;
    }

    private async Task MakeLeavingProcessDueAsync(Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        await db.EmployeeLeavingProcesses
            .IgnoreQueryFilters()
            .Where(p => p.EmployeeId == employeeId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LeavingDate, yesterday));
    }

    private sealed record IdPayload(Guid Id);

    private sealed class StartupOnlyFactory(string environmentName) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment(environmentName);
    }

    /// <summary>Sets E2E_TESTING for the duration of a test and restores the original value on dispose.</summary>
    private sealed class E2eVariable : IDisposable
    {
        private readonly string? _original = Environment.GetEnvironmentVariable("E2E_TESTING");

        public static E2eVariable Set(string? value)
        {
            var scope = new E2eVariable();
            Environment.SetEnvironmentVariable("E2E_TESTING", value);
            return scope;
        }

        public void Dispose() => Environment.SetEnvironmentVariable("E2E_TESTING", _original);
    }
}
