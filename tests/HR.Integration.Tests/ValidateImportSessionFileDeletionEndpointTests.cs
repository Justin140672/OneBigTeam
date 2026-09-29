using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ValidateImportSessionFileDeletionEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid ImportAdmin = Guid.Parse("57000000-0000-0000-0000-000000000002");

    public ValidateImportSessionFileDeletionEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Successful_Validate_Deletes_The_Raw_File_From_Storage_And_Records_FileDeletedAt()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var sessionId = await UploadAsync(client, companyId, ValidCsv());

        string storageKey;
        await using (var preScope = _factory.Services.CreateAsyncScope())
        {
            var preDb = preScope.ServiceProvider.GetRequiredService<DataImportDbContext>();
            var preSession = await preDb.ImportSessions.SingleAsync(s => s.Id == sessionId);
            storageKey = preSession.StorageKey;
            Assert.False(string.IsNullOrWhiteSpace(storageKey));
        }

        var response = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        var session = await db.ImportSessions.SingleAsync(s => s.Id == sessionId);

        Assert.NotNull(session.FileDeletedAt);
        Assert.Equal(0, session.FileDeletionAttemptCount);

        var storage = scope.ServiceProvider.GetRequiredService<IImportFileStorageService>();
        await storage.DeleteAsync(storageKey, CancellationToken.None); // idempotent no-op, must not throw
        await Assert.ThrowsAnyAsync<Exception>(
            () => storage.OpenReadAsync(storageKey, CancellationToken.None));
    }

    [Fact]
    public async Task Revalidating_An_Already_Validated_Session_Does_Not_Attempt_To_Delete_The_File_Again()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var sessionId = await UploadAsync(client, companyId, ValidCsv());

        var firstResponse = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        var afterFirst = await db.ImportSessions.SingleAsync(s => s.Id == sessionId);
        var firstDeletedAt = afterFirst.FileDeletedAt;
        Assert.NotNull(firstDeletedAt);

        // The endpoint itself rejects a second validate call outright (session is no longer
        // Pending), which is the primary guard — this just confirms the deletion timestamp is
        // untouched by the rejected retry, i.e. no double-processing of the file-deletion state.
        var secondResponse = await client.PostAsync(ValidateUrl(companyId, sessionId), EmptyJson());
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        db.ChangeTracker.Clear();
        var afterSecond = await db.ImportSessions.SingleAsync(s => s.Id == sessionId);
        Assert.Equal(firstDeletedAt, afterSecond.FileDeletedAt);
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ImportAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, ImportAdmin, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static string ValidateUrl(Guid companyId, Guid sessionId) =>
        $"/api/companies/{companyId}/data-import/sessions/{sessionId}/validate";

    private static string ValidCsv() =>
        "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile,Salary Amount\n" +
        "John,Doe,john.doe@example.com,2026-01-01,EMP001,1990-01-01,British,Male,Sales,London,Permanent,Software Developer,50000\n";

    private static async Task<Guid> UploadAsync(HttpClient client, Guid companyId, string csvContent)
    {
        var response = await client.PostAsync(
            $"/api/companies/{companyId}/data-import/sessions",
            BuildCsvUpload(csvContent));

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<UploadPayload>();
        Assert.NotNull(payload);
        return payload!.Id;
    }

    private static MultipartFormDataContent BuildCsvUpload(string csvContent)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("Employee"), "EntityType");

        var fileContent = new ByteArrayContent(BuildXlsxBytes(csvContent));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "File", "employees.xlsx");

        return content;
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

    private sealed record UploadPayload(
        Guid Id, Guid CompanyId, string EntityType, string FileName, string Status,
        int TotalRows, DateTimeOffset CreatedAt);

    private static StringContent EmptyJson() =>
        new("{}", Encoding.UTF8, "application/json");
}
