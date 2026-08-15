using System.Net;
using ClosedXML.Excel;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DownloadImportTemplateEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ImportAdmin = Guid.Parse("64000000-0000-0000-0000-000000000001");

    public DownloadImportTemplateEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Ok_With_Xlsx_Content_Type_And_Header_Row()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.GetAsync(TemplateUrl(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Employee Import");

        var usedRange = sheet.RangeUsed();
        Assert.NotNull(usedRange);
        var lastColumn = usedRange!.LastColumn().ColumnNumber();

        var headerCells = new List<string>();
        for (var col = 1; col <= lastColumn; col++)
            headerCells.Add(sheet.Cell(1, col).GetString());

        Assert.Contains("First Name", headerCells);
        Assert.Contains("Last Name", headerCells);
        Assert.Contains("Work Email", headerCells);
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(TemplateUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ImportAdmin, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static string TemplateUrl(Guid companyId) =>
        $"/api/companies/{companyId}/data-import/employees/template";
}
