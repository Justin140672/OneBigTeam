using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ImportDuplicateTitlePositionProfileEndpointTests
{
    private const string Title = "Duplicate Role";
    private const string Header =
        "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile,Salary Amount\n";

    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ImportAdmin = Guid.Parse("59100000-0000-0000-0000-000000000001");

    public ImportDuplicateTitlePositionProfileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Preview_Matches_Each_Row_To_A_Distinct_Duplicate_Profile_Without_Creation_Warnings()
    {
        var (client, companyId, _) = await SeedAsync(profileCount: 2);
        using var _c = client;

        var sessionId = await UploadAndValidateAsync(client, companyId, CsvRows("a", "b"));
        var preview = await GetPreviewAsync(client, companyId, sessionId);

        Assert.Equal(2, preview.ValidRowCount);
        Assert.DoesNotContain(preview.ReferenceDataCreatedWarnings, w => w.Message.Contains("Position Profile"));
    }

    [Fact]
    public async Task Preview_Warns_For_Rows_That_Exceed_Available_Duplicate_Profiles()
    {
        var (client, companyId, _) = await SeedAsync(profileCount: 2);
        using var _c = client;

        var sessionId = await UploadAndValidateAsync(client, companyId, CsvRows("a", "b", "c"));
        var preview = await GetPreviewAsync(client, companyId, sessionId);

        var warning = Assert.Single(preview.ReferenceDataCreatedWarnings, w => w.Message.Contains("Position Profile"));
        Assert.Equal(4, warning.RowNumber);
    }

    [Fact]
    public async Task Preview_Skips_Occupied_Profile_And_Warns_When_No_Unoccupied_Duplicate_Remains()
    {
        var (client, companyId, _) = await SeedAsync(profileCount: 2);
        using var _c = client;

        await ImportAsync(client, companyId, CsvRows("first"));

        var sessionId = await UploadAndValidateAsync(client, companyId, CsvRows("second", "third"));
        var preview = await GetPreviewAsync(client, companyId, sessionId);

        var warning = Assert.Single(preview.ReferenceDataCreatedWarnings, w => w.Message.Contains("Position Profile"));
        Assert.Equal(3, warning.RowNumber);
    }

    [Fact]
    public async Task Confirm_Assigns_Different_Profile_To_Each_Row_And_Never_Reuses_One()
    {
        var (client, companyId, profileIds) = await SeedAsync(profileCount: 2);
        using var _c = client;

        var created = await ImportAsync(client, companyId, CsvRows("a", "b", "c"));

        Assert.Equal(3, created.CreatedCount);
        var assigned = new List<Guid>();
        foreach (var row in created.CreatedRows)
            assigned.Add(await GetEmployeeProfileIdAsync(client, companyId, row.EmployeeId));

        Assert.Equal(3, assigned.Distinct().Count());
        Assert.All(profileIds, id => Assert.Contains(id, assigned));

        var profiles = await ListProfilesAsync(client, companyId);
        Assert.Equal(3, profiles.Count(p => string.Equals(p.Title, Title, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Confirm_Skips_Occupied_Profile_And_Uses_Remaining_Duplicate()
    {
        var (client, companyId, profileIds) = await SeedAsync(profileCount: 2);
        using var _c = client;

        var first = await ImportAsync(client, companyId, CsvRows("first"));
        var firstProfile = await GetEmployeeProfileIdAsync(client, companyId, first.CreatedRows[0].EmployeeId);
        Assert.Contains(firstProfile, profileIds);

        var second = await ImportAsync(client, companyId, CsvRows("second"));
        var secondProfile = await GetEmployeeProfileIdAsync(client, companyId, second.CreatedRows[0].EmployeeId);

        Assert.NotEqual(firstProfile, secondProfile);
        Assert.Contains(secondProfile, profileIds);
        Assert.Equal(2, (await ListProfilesAsync(client, companyId)).Count(p => string.Equals(p.Title, Title, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Confirm_Creates_New_Profile_When_All_Matching_Profiles_Are_Occupied()
    {
        var (client, companyId, profileIds) = await SeedAsync(profileCount: 1);
        using var _c = client;

        await ImportAsync(client, companyId, CsvRows("first"));
        var second = await ImportAsync(client, companyId, CsvRows("second"));
        var secondProfile = await GetEmployeeProfileIdAsync(client, companyId, second.CreatedRows[0].EmployeeId);

        Assert.DoesNotContain(secondProfile, profileIds);
        Assert.Equal(2, (await ListProfilesAsync(client, companyId)).Count(p => string.Equals(p.Title, Title, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<(HttpClient Client, Guid CompanyId, List<Guid> ProfileIds)> SeedAsync(int profileCount)
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Import Duplicate Title Co {Guid.NewGuid():N}");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ImportAdmin, SystemRoles.HrAdministrator, companyId);

        var leavePolicyId = await PostForIdAsync(client, $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Default Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });

        var settings = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", new
        {
            id = companyId,
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 6,
            employeeNumberMode = "Manual",
            employeeNumberPrefix = (string?)null,
            nextEmployeeNumber = 1,
            employeeNumberMinimumLength = 1
        });
        settings.EnsureSuccessStatusCode();

        var departmentId = await PostForIdAsync(client, $"/api/companies/{companyId}/departments", new { companyId, name = "Sales" });
        var locationTypeId = await PostForIdAsync(client, $"/api/companies/{companyId}/location-types", new { companyId, name = "Office" });
        var locationId = await PostForIdAsync(client, $"/api/companies/{companyId}/locations",
            new { companyId, name = "London", locationTypeId });

        var profileIds = new List<Guid>();
        for (var i = 0; i < profileCount; i++)
        {
            profileIds.Add(await PostForIdAsync(client, $"/api/companies/{companyId}/position-profiles", new
            {
                companyId,
                departmentId,
                locationId,
                defaultLeavePolicyId = leavePolicyId,
                title = Title
            }));
            await Task.Delay(20);
        }

        return (client, companyId, profileIds);
    }

    private static async Task<Guid> PostForIdAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private static string CsvRows(params string[] keys)
    {
        var sb = new StringBuilder(Header);
        foreach (var key in keys)
        {
            var unique = Guid.NewGuid().ToString("N")[..8];
            sb.Append($"{key},Person,{key}.{unique}@example.com,2026-01-01,N{unique},1990-01-01,British,Male,Sales,London,Permanent,{Title.ToUpperInvariant()},50000\n");
        }

        return sb.ToString();
    }

    private static async Task<Guid> UploadAndValidateAsync(HttpClient client, Guid companyId, string csv)
    {
        var content = new MultipartFormDataContent { { new StringContent("Employee"), "EntityType" } };
        var file = new ByteArrayContent(BuildXlsxBytes(csv));
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(file, "File", "employees.xlsx");

        var upload = await client.PostAsync($"/api/companies/{companyId}/data-import/sessions", content);
        upload.EnsureSuccessStatusCode();
        var sessionId = (await upload.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var validate = await client.PostAsync(
            $"/api/companies/{companyId}/data-import/sessions/{sessionId}/validate",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        validate.EnsureSuccessStatusCode();
        return sessionId;
    }

    private static async Task<ConfirmPayload> ImportAsync(HttpClient client, Guid companyId, string csv)
    {
        var sessionId = await UploadAndValidateAsync(client, companyId, csv);
        var confirm = await client.PostAsync(
            $"/api/companies/{companyId}/data-import/sessions/{sessionId}/confirm",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var payload = await confirm.Content.ReadFromJsonAsync<ConfirmPayload>();
        Assert.Equal(0, payload!.FailedCount);
        return payload;
    }

    private static async Task<PreviewPayload> GetPreviewAsync(HttpClient client, Guid companyId, Guid sessionId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/data-import/sessions/{sessionId}/preview");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PreviewPayload>())!;
    }

    private static async Task<Guid> GetEmployeeProfileIdAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        return payload!.PositionProfileId!.Value;
    }

    private static async Task<List<ProfilePayload>> ListProfilesAsync(HttpClient client, Guid companyId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/position-profiles");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProfilesPayload>())!.Items;
    }

    private static byte[] BuildXlsxBytes(string csvShapedContent)
    {
        var lines = csvShapedContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToList();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");

        for (var row = 0; row < lines.Count; row++)
        {
            var cells = lines[row].Split(',');
            for (var col = 0; col < cells.Length; col++)
                worksheet.Cell(row + 1, col + 1).Value = cells[col];
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed record IdPayload(Guid Id);

    private sealed record ConfirmedRowPayload(int RowNumber, Guid EmployeeId, string EmployeeNumber);

    private sealed record ConfirmPayload(int CreatedCount, int FailedCount, List<ConfirmedRowPayload> CreatedRows);

    private sealed record PreviewWarningPayload(int RowNumber, string Severity, string Message);

    private sealed record PreviewPayload(int ValidRowCount, int InvalidRowCount, List<PreviewWarningPayload> ReferenceDataCreatedWarnings);

    private sealed record EmployeePayload(Guid Id, Guid? PositionProfileId);

    private sealed record ProfilePayload(Guid Id, string Title);

    private sealed record ProfilesPayload(List<ProfilePayload> Items);
}
