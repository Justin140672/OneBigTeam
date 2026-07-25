using System.Net;
using ClosedXML.Excel;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetCompensationImportTemplateEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid TemplateAdmin = new("cccccccc-1000-0000-0000-000000000001");

    public GetCompensationImportTemplateEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
                await TestRoleSeeder.AssignRoleAsync(factory, TemplateAdmin, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_Template_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/compensation/import-template");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Template_Returns_Ok_With_Xlsx_Content_Type_And_Expected_Sheets()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, TemplateAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        await CompensationTestHelpers.CreateEmployeeWithNumberAsync(client, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/compensation/import-template");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(2, workbook.Worksheets.Count);
        Assert.True(workbook.TryGetWorksheet("Compensation Import", out _));
        Assert.True(workbook.TryGetWorksheet("Instructions", out _));
    }
}
