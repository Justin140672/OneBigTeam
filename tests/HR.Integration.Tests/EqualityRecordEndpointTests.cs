using System.Data;
using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Persistence;
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

    // ── NFR-01 / Ticket 3: no answer values leak into the audit trail ─────────

    [Fact]
    public async Task Put_Records_Only_An_Action_Level_Audit_Event_With_No_Answer_Values()
    {
        var (client, companyId, employeeId) = await EmployeeAsync();

        const string genderFreeText = "AuditTestSelfDescribedGenderText";
        const string disabilityFreeText = "AuditTestDisabilityConditionText";

        var put = await client.PutAsJsonAsync(Route(companyId, employeeId), new
        {
            ethnicGroup = "White",
            religionOrBelief = "Christian",
            disabilityStatus = "Yes",
            disabilityImpact = disabilityFreeText,
            genderIdentity = "SelfDescribed",
            genderIdentitySelfDescribed = genderFreeText
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var (events, rawJson) = await ReadEqualityAuditAsync(companyId, employeeId);

        var updated = Assert.Single(events);
        Assert.Equal("employee.equality_data.updated", updated.EventType);
        Assert.Equal("Equality monitoring data provided", updated.Summary);
        Assert.Equal(employeeId, updated.ActorEmployeeId);
        Assert.NotEqual(default, updated.OccurredAt);

        foreach (var forbidden in new[] { "White", "Christian", "Yes", disabilityFreeText, genderFreeText, "OBTENC1:" })
            Assert.DoesNotContain(forbidden, rawJson, StringComparison.Ordinal);

        // Negative control: the answer column is still encrypted at rest.
        var rawEthnicGroup = await ReadRawColumnAsync(companyId, employeeId, "ethnic_group");
        Assert.StartsWith("OBTENC1:", rawEthnicGroup);
    }

    [Fact]
    public async Task Delete_Records_An_Action_Level_Deleted_Audit_Event_With_No_Values()
    {
        var (client, companyId, employeeId) = await EmployeeAsync();

        await client.PutAsJsonAsync(Route(companyId, employeeId), new
        {
            ethnicGroup = "White",
            religionOrBelief = "Christian",
            disabilityStatus = "Yes"
        });
        var delete = await client.DeleteAsync(Route(companyId, employeeId));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var (events, rawJson) = await ReadEqualityAuditAsync(companyId, employeeId);

        var deleted = Assert.Single(events, e => e.EventType == "employee.equality_data.deleted");
        Assert.Equal("Equality monitoring data withdrawn", deleted.Summary);
        Assert.Equal(employeeId, deleted.ActorEmployeeId);
        Assert.NotEqual(default, deleted.OccurredAt);

        foreach (var forbidden in new[] { "White", "Christian", "Yes", "OBTENC1:" })
            Assert.DoesNotContain(forbidden, rawJson, StringComparison.Ordinal);
    }

    private sealed record EqualityAuditRow(string EventType, string? Summary, Guid? ActorEmployeeId, DateTimeOffset OccurredAt);

    /// <summary>
    /// Returns the promoted equality <see cref="AuditEvent"/> rows for this employee plus the full
    /// concatenated text of every audit artifact (summary + before/after/metadata JSON on the
    /// promoted rows, and the raw staging <c>PayloadJson</c>) so tests can assert no value leaked.
    /// </summary>
    private async Task<(IReadOnlyList<EqualityAuditRow> Events, string RawJson)> ReadEqualityAuditAsync(
        Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var events = await audit.AuditEvents
            .Where(e => e.CompanyId == companyId
                        && e.EmployeeId == employeeId
                        && e.EventType.StartsWith("employee.equality_data."))
            .OrderBy(e => e.OccurredAt)
            .ToListAsync();

        var rows = events
            .Select(e => new EqualityAuditRow(e.EventType, e.Summary, e.ActorEmployeeId, e.OccurredAt))
            .ToList();

        var promotedText = string.Join(
            "|",
            events.SelectMany(e => new[] { e.Summary, e.BeforeJson, e.AfterJson, e.MetadataJson }));

        var eventIds = events.Select(e => e.EventId).ToList();
        var pendingPayloads = await audit.AuditPendingItems
            .Where(p => eventIds.Contains(p.EventId))
            .Select(p => p.PayloadJson)
            .ToListAsync();

        return (rows, promotedText + "|" + string.Join("|", pendingPayloads));
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
