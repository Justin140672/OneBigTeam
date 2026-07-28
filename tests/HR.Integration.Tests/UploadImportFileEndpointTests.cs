using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HR.Integration.Tests;

public class UploadImportFileEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AcmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ImportAdmin   = Guid.Parse("56000000-0000-0000-0000-000000000001");

    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public UploadImportFileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/data-import/sessions",
            BuildCsvUpload("Employee"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_Company_Claim_Mismatches()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());

        // Authenticated user belongs to a different company than the one in the route (cross-tenant).
        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/data-import/sessions",
            BuildCsvUpload("Employee"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_User_Lacks_EmployeeManage_Permission()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());

        // Same company, but no role assigned -> fails the "employee:manage" policy check.
        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/data-import/sessions",
            BuildCsvUpload("Employee"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Created_When_Valid_Csv_Is_Uploaded()
    {
        using var client = AdminClient();

        const string csv = "first_name,last_name,email\nJohn,Doe,john@example.com\nJane,Doe,jane@example.com\n";

        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/data-import/sessions",
            BuildCsvUpload("Employee", csv));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UploadPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(AcmeCompanyId, payload.CompanyId);
        Assert.Equal("Employee", payload.EntityType);
        Assert.Equal("employees.csv", payload.FileName);
        Assert.Equal("Pending", payload.Status);
        Assert.Equal(2, payload.TotalRows); // header row excluded
    }

    [Fact]
    public async Task Returns_Created_When_Valid_Xlsx_Is_Uploaded()
    {
        using var client = AdminClient();
        var xlsxBytes    = BuildXlsxBytes(dataRowCount: 4);

        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/data-import/sessions",
            BuildFileUpload("Employee", xlsxBytes, "employees.xlsx", XlsxContentType));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UploadPayload>();
        Assert.NotNull(payload);
        Assert.Equal("employees.xlsx", payload!.FileName);
        Assert.Equal(4, payload.TotalRows); // header row excluded
    }

    [Fact]
    public async Task Returns_UnprocessableEntity_When_File_Exceeds_Max_Size()
    {
        using var client = AdminClient();

        // Default max is 10 MB; build an oversized CSV payload.
        var oversized = new byte[11 * 1024 * 1024];

        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/data-import/sessions",
            BuildFileUpload("Employee", oversized, "employees.csv", "text/csv"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Returns_UnprocessableEntity_When_Extension_Not_Allowed()
    {
        using var client = AdminClient();

        var response = await client.PostAsync(
            $"/api/companies/{AcmeCompanyId}/data-import/sessions",
            BuildFileUpload("Employee", Encoding.UTF8.GetBytes("not,a,csv"), "employees.txt", "text/plain"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        return client;
    }

    private static MultipartFormDataContent BuildCsvUpload(
        string entityType,
        string csvContent = "first_name,last_name,email\nJohn,Doe,john@example.com\n") =>
        BuildFileUpload(entityType, Encoding.UTF8.GetBytes(csvContent), "employees.csv", "text/csv");

    private static MultipartFormDataContent BuildFileUpload(
        string entityType,
        byte[] fileBytes,
        string fileName,
        string contentType)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(entityType), "EntityType");

        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(fileContent, "File", fileName);

        return content;
    }

    // Builds a real XLSX workbook (header row + dataRowCount data rows) via ClosedXML.
    private static byte[] BuildXlsxBytes(int dataRowCount)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");

        worksheet.Cell(1, 1).Value = "first_name";
        worksheet.Cell(1, 2).Value = "last_name";
        worksheet.Cell(1, 3).Value = "email";

        for (var i = 1; i <= dataRowCount; i++)
        {
            worksheet.Cell(i + 1, 1).Value = $"First{i}";
            worksheet.Cell(i + 1, 2).Value = $"Last{i}";
            worksheet.Cell(i + 1, 3).Value = $"user{i}@example.com";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed record UploadPayload(
        Guid Id, Guid CompanyId, string EntityType, string FileName, string Status,
        int TotalRows, DateTimeOffset CreatedAt);
}
