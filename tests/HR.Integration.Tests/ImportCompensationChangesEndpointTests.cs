using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class ImportCompensationChangesEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ImportAdmin = new("dddddddd-1000-0000-0000-000000000001");

    public ImportCompensationChangesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
                await TestRoleSeeder.AssignRoleAsync(factory, ImportAdmin, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Post_Import_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/companies/{Guid.NewGuid()}/compensation/import",
            BuildUpload(Guid.NewGuid(), BuildWorkbookBytes(("EMP-001", "45000", "Annual", "2027-01-01", "NewHire", null))));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Import_Returns_UnprocessableEntity_For_Wrong_File_Extension()
    {
        // File extension/content-type checks live in ImportCompensationChangesValidator
        // (FluentValidation), not the handler's InvalidFile path — this codebase's established
        // convention is that FastEndpoints validator failures return 422, not 400 (Program.cs —
        // c.Errors.StatusCode = 422; see also CreateAssetCategoryEndpointTests for the equivalent
        // convention on another endpoint).
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var content = new MultipartFormDataContent
        {
            { new StringContent(companyId.ToString()), "CompanyId" }
        };
        var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        content.Add(fileContent, "File", "import.csv");

        var response = await client.PostAsync($"/api/companies/{companyId}/compensation/import", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Import_Returns_UnprocessableEntity_For_Row_Validation_Errors()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var (_, employeeNumber) = await CompensationTestHelpers.CreateEmployeeWithNumberAsync(client, companyId);

        // New Salary is not a positive number.
        var upload = BuildUpload(companyId, BuildWorkbookBytes((employeeNumber, "not-a-number", "Annual", "2027-01-01", "NewHire", null)));

        var response = await client.PostAsync($"/api/companies/{companyId}/compensation/import", upload);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<RowErrorsPayload>();
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Errors);
    }

    [Fact]
    public async Task Post_Import_Creates_Compensation_Records_For_Valid_Rows()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var (employeeId, employeeNumber) = await CompensationTestHelpers.CreateEmployeeWithNumberAsync(client, companyId);

        // Salary Frequency is reference-only on import — the row's frequency is inherited from the
        // employee's existing open compensation record, so one must be seeded first.
        var initial = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/compensation", new
        {
            companyId,
            employeeId,
            effectiveFrom = "2026-01-01",
            salaryType = "Annual",
            salary = 40000m,
            currency = "GBP",
            reason = "NewHire"
        });
        initial.EnsureSuccessStatusCode();

        var upload = BuildUpload(companyId, BuildWorkbookBytes((employeeNumber, "46000", "Annual", "2027-01-01", "AnnualReview", "Starting salary")));

        var response = await client.PostAsync($"/api/companies/{companyId}/compensation/import", upload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ImportResponsePayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.ImportBatchId);
        var item = Assert.Single(payload.Items);
        Assert.Equal(employeeId, item.EmployeeId);
        Assert.Equal(46000m, item.NewSalary);

        var currentResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/current");
        currentResponse.EnsureSuccessStatusCode();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private static MultipartFormDataContent BuildUpload(Guid companyId, byte[] fileBytes)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(companyId.ToString()), "CompanyId" }
        };

        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "File", "import.xlsx");

        return content;
    }

    private static byte[] BuildWorkbookBytes(params (string EmployeeNumber, string? NewSalary, string? SalaryFrequency, string? EffectiveDate, string? Reason, string? Notes)[] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");

        sheet.Cell(1, 1).Value = "Employee Number";
        sheet.Cell(1, 2).Value = "New Salary";
        sheet.Cell(1, 3).Value = "Salary Frequency";
        sheet.Cell(1, 4).Value = "Effective Date";
        sheet.Cell(1, 5).Value = "Reason";
        sheet.Cell(1, 6).Value = "Notes";

        var rowIndex = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.EmployeeNumber;
            if (row.NewSalary is not null) sheet.Cell(rowIndex, 2).Value = row.NewSalary;
            if (row.SalaryFrequency is not null) sheet.Cell(rowIndex, 3).Value = row.SalaryFrequency;
            if (row.EffectiveDate is not null) sheet.Cell(rowIndex, 4).Value = row.EffectiveDate;
            if (row.Reason is not null) sheet.Cell(rowIndex, 5).Value = row.Reason;
            if (row.Notes is not null) sheet.Cell(rowIndex, 6).Value = row.Notes;
            rowIndex++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed record RowErrorPayload(int RowNumber, string Message);
    private sealed record RowErrorsPayload(IReadOnlyList<RowErrorPayload> Errors);

    private sealed record ImportedItemPayload(Guid EmployeeId, string EmployeeNumber, Guid CompensationRecordId, decimal NewSalary, DateOnly EffectiveDate);
    private sealed record ImportResponsePayload(Guid ImportBatchId, IReadOnlyList<ImportedItemPayload> Items);
}
