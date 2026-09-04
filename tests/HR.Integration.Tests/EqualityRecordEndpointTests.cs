using System.Data;
using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// GET/PUT/DELETE <c>/api/companies/{companyId}/employees/{employeeId}/equality-record</c> —
/// voluntary equality-monitoring data. Self-service only (caller must target their own employee id)
/// and answer columns are encrypted at rest.
/// </summary>
[Collection("Integration")]
public class EqualityRecordEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public EqualityRecordEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    private static string Route(Guid companyId, Guid employeeId)
        => $"/api/companies/{companyId}/employees/{employeeId}/equality-record";

    private async Task<(HttpClient Client, Guid CompanyId, Guid EmployeeId)> EmployeeAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return (client, companyId, userId);
    }

    private static object PayloadWithEthnicGroup(string value) => new
    {
        ethnicGroup = value
    };

    // ── 401 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(Route(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(Route(Guid.NewGuid(), Guid.NewGuid()), PayloadWithEthnicGroup("White"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.DeleteAsync(Route(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 403 self-only ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Returns_Forbidden_When_Targeting_A_Different_Employee()
    {
        var (client, companyId, _) = await EmployeeAsync();
        var response = await client.GetAsync(Route(companyId, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns_Forbidden_When_Targeting_A_Different_Employee()
    {
        var (client, companyId, _) = await EmployeeAsync();
        var response = await client.PutAsJsonAsync(Route(companyId, Guid.NewGuid()), PayloadWithEthnicGroup("White"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns_Forbidden_When_Targeting_A_Different_Employee()
    {
        var (client, companyId, _) = await EmployeeAsync();
        var response = await client.DeleteAsync(Route(companyId, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GET initial state ─────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Returns_HasRecord_False_When_No_Record_Exists()
    {
        var (client, companyId, employeeId) = await EmployeeAsync();

        var response = await client.GetAsync(Route(companyId, employeeId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EqualityPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.HasRecord);
        Assert.Null(payload.EthnicGroup);
    }

    // ── PUT create + ciphertext at rest ───────────────────────────────────────

    [Fact]
    public async Task Put_Creates_Record_And_Stores_Answer_Columns_As_Ciphertext()
    {
        var (client, companyId, employeeId) = await EmployeeAsync();

        var putResponse = await client.PutAsJsonAsync(Route(companyId, employeeId), new
        {
            ethnicGroup = "White",
            genderIdentity = "Woman",
            disabilityStatus = "No"
        });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var saved = await putResponse.Content.ReadFromJsonAsync<EqualityPayload>();
        Assert.NotNull(saved);
        Assert.True(saved!.HasRecord);
        Assert.Equal("White", saved.EthnicGroup);

        // GET reflects the saved (decrypted) values.
        var getResponse = await client.GetAsync(Route(companyId, employeeId));
        var fetched = await getResponse.Content.ReadFromJsonAsync<EqualityPayload>();
        Assert.Equal("White", fetched!.EthnicGroup);
        Assert.Equal("Woman", fetched.GenderIdentity);

        // The raw column value must be an OBTENC1 token, not the plaintext enum name.
        var rawEthnicGroup = await ReadRawColumnAsync(companyId, employeeId, "ethnic_group");
        Assert.StartsWith("OBTENC1:", rawEthnicGroup);
        Assert.NotEqual("White", rawEthnicGroup);
    }

    // ── PUT update in place ───────────────────────────────────────────────────

    [Fact]
    public async Task Put_Twice_Updates_In_Place_And_Keeps_A_Single_Row()
    {
        var (client, companyId, employeeId) = await EmployeeAsync();

        await client.PutAsJsonAsync(Route(companyId, employeeId), PayloadWithEthnicGroup("White"));
        var second = await client.PutAsJsonAsync(Route(companyId, employeeId), PayloadWithEthnicGroup("Mixed"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var fetched = await (await client.GetAsync(Route(companyId, employeeId)))
            .Content.ReadFromJsonAsync<EqualityPayload>();
        Assert.Equal("Mixed", fetched!.EthnicGroup);

        Assert.Equal(1, await CountRowsAsync(companyId, employeeId));
    }

    // ── PUT validation failure ────────────────────────────────────────────────

    [Fact]
    public async Task Put_Returns_UnprocessableEntity_When_SelfDescribed_Free_Text_Is_Set_Without_SelfDescribed_Enum()
    {
        var (client, companyId, employeeId) = await EmployeeAsync();

        var response = await client.PutAsJsonAsync(Route(companyId, employeeId), new
        {
            ethnicGroup = "White",
            ethnicGroupSelfDescribed = "Cornish"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── DELETE ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Returns_NotFound_When_No_Record_Exists()
    {
        var (client, companyId, employeeId) = await EmployeeAsync();
        var response = await client.DeleteAsync(Route(companyId, employeeId));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_After_Create_Returns_NoContent_Then_Get_Reports_No_Record()
    {
        var (client, companyId, employeeId) = await EmployeeAsync();

        await client.PutAsJsonAsync(Route(companyId, employeeId), PayloadWithEthnicGroup("White"));

        var deleteResponse = await client.DeleteAsync(Route(companyId, employeeId));
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var payload = await (await client.GetAsync(Route(companyId, employeeId)))
            .Content.ReadFromJsonAsync<EqualityPayload>();
        Assert.False(payload!.HasRecord);

        // A second delete is a no-op 404.
        var secondDelete = await client.DeleteAsync(Route(companyId, employeeId));
        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<string> ReadRawColumnAsync(Guid companyId, Guid employeeId, string column)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT {column} FROM employees.employee_equality_data " +
                "WHERE company_id = @company AND employee_id = @employee";
            command.Parameters.Add(Param(command, "@company", companyId));
            command.Parameters.Add(Param(command, "@employee", employeeId));
            var value = await command.ExecuteScalarAsync();
            return (string)value!;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private async Task<int> CountRowsAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM employees.employee_equality_data " +
                "WHERE company_id = @company AND employee_id = @employee";
            command.Parameters.Add(Param(command, "@company", companyId));
            command.Parameters.Add(Param(command, "@employee", employeeId));
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static IDbDataParameter Param(IDbCommand command, string name, object value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        return p;
    }

    private sealed record EqualityPayload(
        bool HasRecord,
        string? GenderIdentity,
        string? GenderIdentitySelfDescribed,
        string? MarriedOrCivilPartnershipStatus,
        string? EthnicGroup,
        string? EthnicGroupSelfDescribed,
        string? DisabilityStatus,
        string? DisabilityImpact,
        string? SexualOrientation,
        string? SexualOrientationSelfDescribed,
        string? ReligionOrBelief,
        string? ReligionOrBeliefSelfDescribed,
        DateTimeOffset? CreatedAt,
        DateTimeOffset? UpdatedAt);
}
