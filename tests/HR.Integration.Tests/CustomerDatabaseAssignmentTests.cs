using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.CustomerDatabase;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 1: Customer Database Assignment integration tests covering both the GetCustomerDatabaseAssignment
/// endpoint (authorization and various assignment states) and the CustomerDatabaseConnection resolver
/// (connection resolution, caching, and error handling).
/// </summary>
[Collection("Integration")]
public class GetCustomerDatabaseAssignmentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetCustomerDatabaseAssignmentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClientAsync(Guid? userId = null, string? email = null)
    {
        var resolvedUserId = userId ?? Guid.NewGuid();
        await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory,
            HR.Modules.Identity.Domain.PlatformAdministratorRole.SupportStaff,
            isEnabled: true,
            supabaseAuthUserId: resolvedUserId,
            email: email);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, resolvedUserId.ToString());
        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        }

        return client;
    }

    private async Task SeedCustomerDatabaseAssignmentAsync(
        Guid companyId,
        CustomerDatabaseAssignmentStatus status,
        string? databaseKey = null,
        uint? schemaOid = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var assignment = CustomerDatabaseAssignment.Create(Guid.NewGuid(), companyId, DateTimeOffset.UtcNow);

        if (!string.IsNullOrEmpty(databaseKey))
        {
            assignment.SetDatabaseKey(databaseKey, DateTimeOffset.UtcNow);
        }

        if (schemaOid.HasValue)
        {
            assignment.SetSchemaOid(schemaOid.Value, DateTimeOffset.UtcNow);
        }

        if (status == CustomerDatabaseAssignmentStatus.Active)
        {
            assignment.Activate(DateTimeOffset.UtcNow);
        }
        else if (status == CustomerDatabaseAssignmentStatus.Inactive)
        {
            assignment.Deactivate(DateTimeOffset.UtcNow);
        }

        db.CustomerDatabaseAssignments.Add(assignment);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_CustomerDatabaseAssignment_Returns_Unauthorized_For_Anonymous_Request()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/platform-admin/companies/{companyId}/database-assignment");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_CustomerDatabaseAssignment_Returns_Forbidden_For_Non_Platform_Admin()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);
        var userId = Guid.NewGuid();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());

        var response = await client.GetAsync($"/api/platform-admin/companies/{companyId}/database-assignment");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_CustomerDatabaseAssignment_Returns_NotFound_When_No_Assignment_Exists()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);
        using var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync($"/api/platform-admin/companies/{companyId}/database-assignment");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_CustomerDatabaseAssignment_Returns_200_With_Active_Assignment_And_DatabaseKey()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);
        const string databaseKey = "cust-test-db1";
        const uint schemaOid = 12345;

        await SeedCustomerDatabaseAssignmentAsync(
            companyId,
            CustomerDatabaseAssignmentStatus.Active,
            databaseKey: databaseKey,
            schemaOid: schemaOid);

        using var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync($"/api/platform-admin/companies/{companyId}/database-assignment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<GetCustomerDatabaseAssignmentPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);
        Assert.Equal(CustomerDatabaseAssignmentStatus.Active, payload.Status);
        Assert.Equal(databaseKey, payload.DatabaseKey);
        Assert.Equal(schemaOid, payload.SchemaOid);
    }

    [Fact]
    public async Task Get_CustomerDatabaseAssignment_Returns_200_With_Pending_Assignment_And_No_DatabaseKey()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);

        await SeedCustomerDatabaseAssignmentAsync(
            companyId,
            CustomerDatabaseAssignmentStatus.Pending,
            databaseKey: null);

        using var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync($"/api/platform-admin/companies/{companyId}/database-assignment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<GetCustomerDatabaseAssignmentPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);
        Assert.Equal(CustomerDatabaseAssignmentStatus.Pending, payload.Status);
        Assert.Null(payload.DatabaseKey);
        Assert.Null(payload.SchemaOid);
    }

    [Fact]
    public async Task Get_CustomerDatabaseAssignment_Returns_200_With_Inactive_Assignment()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);
        const string databaseKey = "cust-inactive-db";

        await SeedCustomerDatabaseAssignmentAsync(
            companyId,
            CustomerDatabaseAssignmentStatus.Inactive,
            databaseKey: databaseKey);

        using var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync($"/api/platform-admin/companies/{companyId}/database-assignment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<GetCustomerDatabaseAssignmentPayload>();
        Assert.NotNull(payload);
        Assert.Equal(CustomerDatabaseAssignmentStatus.Inactive, payload!.Status);
        Assert.Equal(databaseKey, payload.DatabaseKey);
    }

    internal sealed record GetCustomerDatabaseAssignmentPayload(
        Guid CompanyId,
        [property: System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<CustomerDatabaseAssignmentStatus>))]
        CustomerDatabaseAssignmentStatus Status,
        string? DatabaseKey,
        uint? SchemaOid,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}

/// <summary>
/// Tests for CustomerDatabaseConnection resolver: connection resolution, caching behavior,
/// and error handling for various assignment states and configuration scenarios.
/// </summary>
[Collection("Integration")]
public class CustomerDatabaseConnectionTests
{
    private readonly ApiWebApplicationFactory _factory;

    public CustomerDatabaseConnectionTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedCustomerDatabaseAssignmentAsync(
        Guid companyId,
        CustomerDatabaseAssignmentStatus status,
        string? databaseKey = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var assignment = CustomerDatabaseAssignment.Create(Guid.NewGuid(), companyId, DateTimeOffset.UtcNow);

        if (!string.IsNullOrEmpty(databaseKey))
        {
            assignment.SetDatabaseKey(databaseKey, DateTimeOffset.UtcNow);
        }

        if (status == CustomerDatabaseAssignmentStatus.Active)
        {
            assignment.Activate(DateTimeOffset.UtcNow);
        }
        else if (status == CustomerDatabaseAssignmentStatus.Inactive)
        {
            assignment.Deactivate(DateTimeOffset.UtcNow);
        }

        db.CustomerDatabaseAssignments.Add(assignment);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetConnectionStringAsync_Throws_For_Company_With_No_Assignment()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<CustomerDatabaseConnection>();

        var exception = await Assert.ThrowsAsync<CustomerDatabaseUnavailableException>(
            () => resolver.GetConnectionStringAsync(companyId));

        Assert.Equal(companyId, exception.CompanyId);
        Assert.Contains("No active database assignment found", exception.Message);
    }

    [Fact]
    public async Task GetConnectionStringAsync_Throws_For_Pending_Assignment()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);

        await SeedCustomerDatabaseAssignmentAsync(
            companyId,
            CustomerDatabaseAssignmentStatus.Pending,
            databaseKey: null);

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<CustomerDatabaseConnection>();

        var exception = await Assert.ThrowsAsync<CustomerDatabaseUnavailableException>(
            () => resolver.GetConnectionStringAsync(companyId));

        Assert.Equal(companyId, exception.CompanyId);
        Assert.Contains("No active database assignment found", exception.Message);
    }

    [Fact]
    public async Task GetConnectionStringAsync_Throws_For_Inactive_Assignment()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);

        await SeedCustomerDatabaseAssignmentAsync(
            companyId,
            CustomerDatabaseAssignmentStatus.Inactive,
            databaseKey: "cust-inactive-db");

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<CustomerDatabaseConnection>();

        var exception = await Assert.ThrowsAsync<CustomerDatabaseUnavailableException>(
            () => resolver.GetConnectionStringAsync(companyId));

        Assert.Equal(companyId, exception.CompanyId);
        Assert.Contains("No active database assignment found", exception.Message);
    }

    [Fact]
    public async Task GetConnectionStringAsync_Returns_ConfiguredConnectionString_For_Active_Assignment_With_DatabaseKey()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);
        const string databaseKey = "cust-test-db1";

        await SeedCustomerDatabaseAssignmentAsync(
            companyId,
            CustomerDatabaseAssignmentStatus.Active,
            databaseKey: databaseKey);

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<CustomerDatabaseConnection>();

        var connectionString = await resolver.GetConnectionStringAsync(companyId);

        // The test config includes a valid connection string for "cust-test-db1"
        Assert.NotNull(connectionString);
        Assert.NotEmpty(connectionString);
        Assert.Contains("Host=", connectionString); // PostgreSQL connection string pattern
    }

    [Fact]
    public async Task GetConnectionStringAsync_Returns_Null_For_Active_Assignment_With_No_DatabaseKey()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);

        await SeedCustomerDatabaseAssignmentAsync(
            companyId,
            CustomerDatabaseAssignmentStatus.Active,
            databaseKey: null);

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<CustomerDatabaseConnection>();

        var connectionString = await resolver.GetConnectionStringAsync(companyId);

        // Active without a database key signals shared database
        Assert.Null(connectionString);
    }

    [Fact]
    public async Task GetConnectionStringAsync_Throws_When_DatabaseKey_Not_Configured()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);
        const string missingDatabaseKey = "cust-missing-config";

        await SeedCustomerDatabaseAssignmentAsync(
            companyId,
            CustomerDatabaseAssignmentStatus.Active,
            databaseKey: missingDatabaseKey);

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<CustomerDatabaseConnection>();

        var exception = await Assert.ThrowsAsync<CustomerDatabaseUnavailableException>(
            () => resolver.GetConnectionStringAsync(companyId));

        Assert.Equal(companyId, exception.CompanyId);
        Assert.Contains("not configured", exception.Message);
    }

    [Fact]
    public async Task GetConnectionStringAsync_Caches_Result_For_60_Seconds()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);
        const string databaseKey = "cust-test-db1";

        await SeedCustomerDatabaseAssignmentAsync(
            companyId,
            CustomerDatabaseAssignmentStatus.Active,
            databaseKey: databaseKey);

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<CustomerDatabaseConnection>();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        // First call should query the database
        var firstConnectionString = await resolver.GetConnectionStringAsync(companyId);
        Assert.NotNull(firstConnectionString);

        // Delete the assignment from the database
        var assignment = await db.CustomerDatabaseAssignments
            .FirstAsync(a => a.CompanyId == companyId);
        db.CustomerDatabaseAssignments.Remove(assignment);
        await db.SaveChangesAsync();

        // Second call within 60 seconds should still return the cached result
        var secondConnectionString = await resolver.GetConnectionStringAsync(companyId);
        Assert.Equal(firstConnectionString, secondConnectionString);
    }

    [Fact]
    public async Task GetConnectionStringAsync_Caches_Null_Result_For_60_Seconds()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory);

        await SeedCustomerDatabaseAssignmentAsync(
            companyId,
            CustomerDatabaseAssignmentStatus.Active,
            databaseKey: null);

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<CustomerDatabaseConnection>();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        // First call should return null (shared database) and cache the result
        var firstResult = await resolver.GetConnectionStringAsync(companyId);
        Assert.Null(firstResult);

        // Delete the assignment from the database
        var assignment = await db.CustomerDatabaseAssignments
            .FirstAsync(a => a.CompanyId == companyId);
        db.CustomerDatabaseAssignments.Remove(assignment);
        await db.SaveChangesAsync();

        // Second call within 60 seconds should still return the cached null result (not throw)
        var secondResult = await resolver.GetConnectionStringAsync(companyId);
        Assert.Null(secondResult);
    }
}
