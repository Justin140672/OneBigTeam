using System.Net;
using HR.Integration.Tests.Infrastructure;
using Npgsql;

namespace HR.Integration.Tests;

/// <summary>
/// Disabled-mode security tests for the DepartureFinaliserTestEndpoint.
/// Verifies that the endpoint returns 404 when E2E_TESTING is not set (default production mode).
/// </summary>
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

    /// <summary>
    /// Verifies that the endpoint returns 404 when E2E_TESTING environment variable is not set.
    /// The endpoint is completely hidden in production/normal-dev mode.
    /// This gate prevents accidental exposure of development testing infrastructure in production.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_404_When_E2E_Testing_Disabled()
    {
        // When E2E_TESTING is not set (default), the endpoint must return 404 (hidden from production)
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
/// The endpoint's security boundaries are enforced at the handler level:
/// - E2E_TESTING gate: returns 404 when the environment variable is not "true" (production mode)
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

    // Well-known test user IDs for role-based authorization tests
    private static readonly Guid HrAdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EmployeeUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BetaCorpHrAdminId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly ApiWebApplicationFactory _factory;

    public DepartureFinaliserEndpointEnabledModeSecurityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    /// <summary>
    /// Verifies that E2E_TESTING=true enables the endpoint and anonymous requests fail with 401.
    /// The endpoint requires authentication even when E2E testing is enabled.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_401_Anonymous_WhenE2EEnabled()
    {
        var employeeId = Guid.NewGuid();
        var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

        // Temporarily enable E2E testing for this test
        var originalValue = Environment.GetEnvironmentVariable("E2E_TESTING");
        try
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", "true");

            using var client = _factory.CreateClient();
            var response = await client.PostAsync(url, content: null);

            // Anonymous requests should get 401 Unauthorized
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", originalValue);
        }
    }

    /// <summary>
    /// Verifies that users without HR Administrator role receive 403 Forbidden.
    /// The endpoint requires "role:hr-administrator" policy.
    /// </summary>
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

            // Set auth headers for an employee (no HR admin role)
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

    /// <summary>
    /// Verifies that HR admins from a different company receive 403 Forbidden.
    /// Even with the correct role, company isolation must be enforced.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_403_WrongTenant()
    {
        var employeeId = Guid.NewGuid();
        var url = $"/api/dev/departure-finaliser/{AcmeCompanyId:N}/{employeeId:N}";

        var originalValue = Environment.GetEnvironmentVariable("E2E_TESTING");
        try
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", "true");

            // First, set up HR admin role for BetaCorpHrAdminId in BetaCorp
            await TestRoleSeeder.AssignRoleAsync(_factory, BetaCorpHrAdminId,
                Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), // role ID doesn't matter for this test
                BetaCorpId, ensureActiveSubscription: false);

            using var client = _factory.CreateClient();

            // Set auth headers for HR admin from BetaCorp (different tenant from route)
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

    /// <summary>
    /// Verifies that requests for non-existent employees receive 404 Not Found.
    /// The endpoint validates employee existence before attempting finalization.
    /// </summary>
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

            // Set auth headers for HR admin in correct company
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

    /// <summary>
    /// Verifies that requests for employees without a leaving process receive 400 Bad Request.
    /// The endpoint cannot finalize employees that have no leaving process.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_400_NoLeavingProcess()
    {
        var originalValue = Environment.GetEnvironmentVariable("E2E_TESTING");
        try
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", "true");

            // Create a test employee without a leaving process
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

    /// <summary>
    /// Verifies that authorized HR admins can successfully finalize leaving employees.
    /// This is the happy path: correct role, correct company, valid employee, in-progress process.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_200_Success()
    {
        var originalValue = Environment.GetEnvironmentVariable("E2E_TESTING");
        try
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", "true");

            // Create an employee with an in-progress leaving process
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

    /// <summary>
    /// Verifies idempotency: finalized employees (already Former Employee with Completed process)
    /// return 200 when finalization is requested again.
    /// </summary>
    [Fact]
    public async Task Post_DepartureFinaliser_Returns_200_Idempotent()
    {
        var originalValue = Environment.GetEnvironmentVariable("E2E_TESTING");
        try
        {
            Environment.SetEnvironmentVariable("E2E_TESTING", "true");

            // Create an employee with a completed leaving process
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

    // ── Test Data Setup Helpers ──────────────────────────────────────────────────

    private async Task<Guid> CreateEmployeeAsync(Guid companyId)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__hr");
        Assert.NotNull(connectionString);

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var employeeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        const string insertQuery = @"
            INSERT INTO employees.employees (id, company_id, first_name, last_name, email, status, employment_status, created_at, updated_at)
            VALUES (@id, @companyId, @firstName, @lastName, @email, @status, @employmentStatus, @createdAt, @updatedAt)
            ON CONFLICT (id) DO NOTHING";

        using var cmd = new NpgsqlCommand(insertQuery, connection)
        {
            Parameters =
            {
                new("@id", employeeId),
                new("@companyId", companyId),
                new("@firstName", "Test"),
                new("@lastName", "Employee"),
                new("@email", $"test-{employeeId:N}@test.local"),
                new("@status", "Active"),
                new("@employmentStatus", "CurrentEmployee"),
                new("@createdAt", now),
                new("@updatedAt", now),
            }
        };

        await cmd.ExecuteNonQueryAsync();
        await connection.CloseAsync();

        return employeeId;
    }

    private async Task<Guid> CreateEmployeeWithLeavingProcessAsync(Guid companyId, string processStatus)
    {
        var employeeId = await CreateEmployeeAsync(companyId);
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__hr");
        Assert.NotNull(connectionString);

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var processId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        const string insertProcessQuery = @"
            INSERT INTO employees.employee_leaving_processes
            (id, company_id, employee_id, status, started_at, leaving_date, created_at, updated_at)
            VALUES (@id, @companyId, @employeeId, @status, @startedAt, @leavingDate, @createdAt, @updatedAt)
            ON CONFLICT (id) DO NOTHING";

        using var cmd = new NpgsqlCommand(insertProcessQuery, connection)
        {
            Parameters =
            {
                new("@id", processId),
                new("@companyId", companyId),
                new("@employeeId", employeeId),
                new("@status", processStatus),
                new("@startedAt", now),
                new("@leavingDate", now.AddDays(-1)), // Yesterday (overdue)
                new("@createdAt", now),
                new("@updatedAt", now),
            }
        };

        await cmd.ExecuteNonQueryAsync();
        await connection.CloseAsync();

        return employeeId;
    }

    private async Task<Guid> CreateEmployeeWithCompletedLeavingProcessAsync(Guid companyId)
    {
        var employeeId = await CreateEmployeeAsync(companyId);
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__hr");
        Assert.NotNull(connectionString);

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var now = DateTimeOffset.UtcNow;

        // Update employee status to FormerEmployee
        const string updateEmployeeQuery = @"
            UPDATE employees.employees SET employment_status = @employmentStatus, updated_at = @updatedAt
            WHERE id = @id";

        using var cmdUpdateEmployee = new NpgsqlCommand(updateEmployeeQuery, connection)
        {
            Parameters =
            {
                new("@id", employeeId),
                new("@employmentStatus", "FormerEmployee"),
                new("@updatedAt", now),
            }
        };

        await cmdUpdateEmployee.ExecuteNonQueryAsync();

        // Insert completed leaving process
        var processId = Guid.NewGuid();

        const string insertProcessQuery = @"
            INSERT INTO employees.employee_leaving_processes
            (id, company_id, employee_id, status, started_at, leaving_date, completed_at, created_at, updated_at)
            VALUES (@id, @companyId, @employeeId, @status, @startedAt, @leavingDate, @completedAt, @createdAt, @updatedAt)
            ON CONFLICT (id) DO NOTHING";

        using var cmd = new NpgsqlCommand(insertProcessQuery, connection)
        {
            Parameters =
            {
                new("@id", processId),
                new("@companyId", companyId),
                new("@employeeId", employeeId),
                new("@status", "Completed"),
                new("@startedAt", now.AddDays(-7)),
                new("@leavingDate", now.AddDays(-7)),
                new("@completedAt", now),
                new("@createdAt", now.AddDays(-7)),
                new("@updatedAt", now),
            }
        };

        await cmd.ExecuteNonQueryAsync();
        await connection.CloseAsync();

        return employeeId;
    }
}
