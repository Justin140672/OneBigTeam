using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// GET <c>/api/companies/{companyId}/reporting/equality-diversity</c> (Ticket 6B) — the anonymous,
/// aggregated Equality &amp; Diversity workforce report. Gated on <c>reporting:view-equality</c>
/// (HR Administrator only). Returns counts/percentages only; small groups are suppressed.
/// </summary>
[Collection("Integration")]
public class EqualityDiversityReportEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public EqualityDiversityReportEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    private static string Route(Guid companyId) => $"/api/companies/{companyId}/reporting/equality-diversity";

    private async Task<HttpClient> ClientForRoleAsync(Guid companyId, Guid userId, Guid roleId)
    {
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    /// <summary>Seeds 15 employees: 7 White, 3 Mixed, 3 Asian equality records, 2 with none.</summary>
    private async Task<List<Guid>> SeedWorkforceAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
        var now = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();

        var ethnic = new[]
        {
            EthnicGroup.White, EthnicGroup.White, EthnicGroup.White, EthnicGroup.White,
            EthnicGroup.White, EthnicGroup.White, EthnicGroup.White,
            EthnicGroup.Mixed, EthnicGroup.Mixed, EthnicGroup.Mixed,
            EthnicGroup.AsianOrAsianBritish, EthnicGroup.AsianOrAsianBritish, EthnicGroup.AsianOrAsianBritish,
        };

        for (var i = 0; i < 15; i++)
        {
            var empId = Guid.NewGuid();
            ids.Add(empId);
            db.Employees.Add(Employee.Create(
                empId, companyId, "Emp", $"N{i}", $"emp{i}.{empId:N}@example.com",
                new DateOnly(2024, 1, 1), hasSystemAccess: false, new DateOnly(1990, 1, 1),
                "British", "Prefer not to say", $"EMP-{empId:N}",
                refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId, now));

            if (i < ethnic.Length)
            {
                db.EmployeeEqualityData.Add(EmployeeEqualityData.Create(
                    Guid.NewGuid(), companyId, empId,
                    null, null, null,
                    ethnic[i].ToString(), null,
                    null, null, null, null, null, null, null,
                    now));
            }
        }

        await db.SaveChangesAsync();
        return ids;
    }

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(Route(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(nameof(SystemRoles.Employee))]
    [InlineData(nameof(SystemRoles.Manager))]
    [InlineData(nameof(SystemRoles.Recruiter))]
    public async Task Returns_Forbidden_For_Roles_Without_ReportingViewEquality(string roleName)
    {
        var roleId = roleName switch
        {
            nameof(SystemRoles.Employee) => SystemRoles.Employee,
            nameof(SystemRoles.Manager) => SystemRoles.Manager,
            _ => SystemRoles.Recruiter,
        };
        var companyId = Guid.NewGuid();
        using var client = await ClientForRoleAsync(companyId, Guid.NewGuid(), roleId);

        var response = await client.GetAsync(Route(companyId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Ok_For_HrAdministrator_With_The_Expected_Anonymous_Shape()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientForRoleAsync(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var employeeIds = await SeedWorkforceAsync(companyId);

        var response = await client.GetAsync(Route(companyId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Equal(5, payload!.MinimumGroupSize);
        Assert.Equal(15, payload.TotalEmployees);
        Assert.Equal(13, payload.RespondentCount);
        Assert.True(payload.RespondentPercentage is > 0 and < 100);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), payload.ReportingDate);

        Assert.Equal(
            new[] { "gender", "age-band", "ethnicity", "disability", "sexual-orientation", "religion-or-belief", "caring-responsibilities" }
                .OrderBy(x => x),
            payload.Dimensions.Select(d => d.Key).OrderBy(x => x));

        // Counts are whole numbers.
        Assert.All(payload.Dimensions.SelectMany(d => d.Rows), r => Assert.True(r.Count >= 0));

        // No employee / company identifier anywhere in the payload.
        var raw = await response.Content.ReadAsStringAsync();
        foreach (var id in employeeIds)
            Assert.DoesNotContain(id.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(companyId.ToString(), raw, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(raw);
        AssertNoGuids(doc.RootElement);
    }

    [Fact]
    public async Task Small_Group_Only_Appears_Inside_A_Suppressed_Not_Reported_Row()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientForRoleAsync(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        await SeedWorkforceAsync(companyId);

        var payload = await (await client.GetAsync(Route(companyId))).Content.ReadFromJsonAsync<ReportPayload>();

        var ethnicity = Assert.Single(payload!.Dimensions, d => d.Key == "ethnicity");
        Assert.DoesNotContain(ethnicity.Rows, r => r.Value == "Mixed");
        Assert.DoesNotContain(ethnicity.Rows, r => r.Value == "Asian Or Asian British");
        // "Not stated" is itself an aggregate bucket and is never suppressed, so exclude it here.
        Assert.DoesNotContain(ethnicity.Rows, r => r.Value != "Not stated" && r.Count is >= 1 and < 5 && !r.Suppressed);

        var notReported = Assert.Single(ethnicity.Rows, r => r.Value == "Not reported");
        Assert.True(notReported.Suppressed);
        Assert.Equal(6, notReported.Count);
        Assert.Equal(7, Assert.Single(ethnicity.Rows, r => r.Value == "White").Count);
    }

    private static void AssertNoGuids(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                    AssertNoGuids(prop.Value);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    AssertNoGuids(item);
                break;
            case JsonValueKind.String:
                Assert.False(Guid.TryParse(element.GetString(), out _), $"Unexpected GUID in payload: {element.GetString()}");
                break;
        }
    }

    private sealed record ReportPayload(
        int TotalEmployees,
        int RespondentCount,
        decimal RespondentPercentage,
        DateOnly ReportingDate,
        int MinimumGroupSize,
        List<DimensionPayload> Dimensions);

    private sealed record DimensionPayload(string Key, string Name, List<RowPayload> Rows);

    private sealed record RowPayload(string Value, int Count, decimal Percentage, bool Suppressed);
}
