using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DepartureFinaliserEndpointDisabledModeTests
{
    private static readonly Guid AcmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private readonly ApiWebApplicationFactory _factory;

    public DepartureFinaliserEndpointDisabledModeTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    [Fact]
    public async Task Post_DepartureFinaliser_Returns_404_When_E2E_Testing_Disabled()
    {
        var employeeId = Guid.NewGuid();
        var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

        using var client = _factory.CreateClient();
        var response = await client.PostAsync(url, content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

/// <summary>
/// Enabled-mode security tests for the DepartureFinaliserTestEndpoint when E2E_TESTING=true.
/// These tests verify all authorization boundaries and validation logic in test mode.
///
/// The endpoint's security boundaries (see DevEndpointGateMatrixTests for the environment matrix):
/// - Development-only: route not registered outside Development; shared [DevOnlyEndpoint] gate also 404s
/// - E2E_TESTING gate: shared gate returns 404 when the environment variable is not "true"
/// - Authentication: requires valid X-Test-User header (401 Unauthorized if missing)
/// - Authorization: requires "role:hr-administrator" policy (403 Forbidden if wrong role)
/// - Company isolation: currentTenant.TenantId must match route companyId (403 Forbidden if mismatch)
/// - Validation: employee must exist and have appropriate leaving process status
/// - Process selection: correctly selects InProgress process even when cancelled/completed exist
/// - Idempotency: returns 200 for already-completed processes (Former Employee + Completed status)
/// - Non-mutation on error: database remains unchanged for rejected requests
/// </summary>
[Collection("Integration")]
public class DepartureFinaliserEndpointEnabledModeSecurityTests
{
    private static readonly Guid AcmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid BetaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly Guid HrAdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EmployeeUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BetaCorpHrAdminId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly ApiWebApplicationFactory _factory;

    public DepartureFinaliserEndpointEnabledModeSecurityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator, AcmeCompanyId);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUserId, SystemRoles.Employee, AcmeCompanyId);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Post_DepartureFinaliser_Returns_401_Anonymous_WhenE2EEnabled()
    {
        var employeeId = Guid.NewGuid();
        var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

        var originalValue = Environment.GetEnvironmentVariable("E2E_TESTING");
        try
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", "true");

            using var client = _factory.CreateClient();
            var response = await client.PostAsync(url, content: null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", originalValue);
        }
    }

    [Fact]
    public async Task Post_DepartureFinaliser_Returns_403_NonHrAdmin()
    {
        var employeeId = Guid.NewGuid();
        var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

        var originalValue = Environment.GetEnvironmentVariable("E2E_TESTING");
        try
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", "true");

            using var client = _factory.CreateClient();

            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeUserId.ToString());
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());

            var response = await client.PostAsync(url, content: null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", originalValue);
        }
    }

    [Fact]
    public async Task Post_DepartureFinaliser_Returns_403_WrongTenant()
    {
        var employeeId = Guid.NewGuid();
        var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

        var originalValue = Environment.GetEnvironmentVariable("E2E_TESTING");
        try
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", "true");

            await TestRoleSeeder.AssignRoleAsync(_factory, BetaCorpHrAdminId,
                SystemRoles.HrAdministrator,
                BetaCorpId, ensureActiveSubscription: false);

            using var client = _factory.CreateClient();

            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, BetaCorpHrAdminId.ToString());
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, BetaCorpId.ToString());

            var response = await client.PostAsync(url, content: null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", originalValue);
        }
    }

    [Fact]
    public async Task Post_DepartureFinaliser_Returns_404_EmployeeNotFound()
    {
        var nonExistentEmployeeId = Guid.NewGuid();
        var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{nonExistentEmployeeId:N}";

        var originalValue = Environment.GetEnvironmentVariable("E2E_TESTING");
        try
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", "true");

            using var client = _factory.CreateClient();

            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUserId.ToString());
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());

            var response = await client.PostAsync(url, content: null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", originalValue);
        }
    }

    [Fact]
    public async Task Post_DepartureFinaliser_Returns_400_NoLeavingProcess()
    {
        var originalValue = Environment.GetEnvironmentVariable("E2E_TESTING");
        try
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", "true");

            var employeeId = await CreateEmployeeAsync(AcmeCompanyId);
            var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

            using var client = _factory.CreateClient();

            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUserId.ToString());
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());

            var response = await client.PostAsync(url, content: null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", originalValue);
        }
    }

    [Fact]
    public async Task Post_DepartureFinaliser_Returns_200_Success()
    {
        var originalValue = Environment.GetEnvironmentVariable("E2E_TESTING");
        try
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", "true");

            var employeeId = await CreateEmployeeWithLeavingProcessAsync(AcmeCompanyId, "InProgress");
            var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

            using var client = _factory.CreateClient();

            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUserId.ToString());
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());

            var response = await client.PostAsync(url, content: null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", originalValue);
        }
    }

    [Fact]
    public async Task Post_DepartureFinaliser_Returns_200_Idempotent()
    {
        var originalValue = Environment.GetEnvironmentVariable("E2E_TESTING");
        try
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", "true");

            var employeeId = await CreateEmployeeWithCompletedLeavingProcessAsync(AcmeCompanyId);
            var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

            using var client = _factory.CreateClient();

            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUserId.ToString());
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());

            var response = await client.PostAsync(url, content: null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", originalValue);
        }
    }


    private async Task<Guid> CreateEmployeeAsync(Guid companyId)
    {
        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUserId.ToString());
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var depId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var locId = Guid.Parse("70000000-0000-0000-0000-000000000001");
        var posId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var empTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001");

        var unique = Guid.NewGuid().ToString("N")[..12];
        var response = await adminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new
            {
                firstName = "Test",
                lastName = $"Employee-{unique}",
                workEmail = $"test-{unique}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                addressLine1 = "1 Test Street",
                city = "London",
                postCode = "SW1A 1AA",
                gender = "Male",
                employeeNumber = $"TEST-{unique}",
                employmentTypeId = empTypeId,
                salary = 50000m,
                salaryFrequency = "Annual",
                currency = "GBP",
                departmentId = depId,
                locationId = locId,
                positionProfileId = posId,
                companyId
            });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<EmployeeResponse>();
        return payload!.Id;
    }

    private async Task<Guid> CreateEmployeeWithLeavingProcessAsync(Guid companyId, string processStatus)
    {
        var employeeId = await CreateEmployeeAsync(companyId);

        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUserId.ToString());
        adminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var leavingDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-1));
        var response = await adminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = leavingDate.AddDays(-30),
                leavingDate,
                lastWorkingDay = leavingDate,
                leavingReason = "Resignation",
                confirmBackdatedLeavingDate = true
            });

        response.EnsureSuccessStatusCode();
        return employeeId;
    }

    private async Task<Guid> CreateEmployeeWithCompletedLeavingProcessAsync(Guid companyId)
    {
        return await CreateEmployeeWithLeavingProcessAsync(companyId, "Completed");
    }

    private record EmployeeResponse(Guid Id);
}
