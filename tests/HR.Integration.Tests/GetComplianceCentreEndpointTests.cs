using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// ADM-02 Compliance Centre endpoint coverage. The endpoint is gated by the dedicated
/// <c>compliance:view</c> policy (HR Administrator only) and every underlying reader is company
/// scoped by the route company id — the tests below lock both the authorization gate and the
/// company-isolation guarantee, plus the end-to-end severity/summary computation.
/// </summary>
[Collection("Integration")]
public class GetComplianceCentreEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.Date);

    public GetComplianceCentreEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string Url(Guid companyId) =>
        $"/api/companies/{companyId}/reporting/compliance-centre";

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId, Guid roleId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);
        return client;
    }

    // ── authorization ──────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static IEnumerable<object[]> ForbiddenRoles() => new[]
    {
        new object[] { SystemRoles.Employee },
        new object[] { SystemRoles.Manager },
        new object[] { SystemRoles.Recruiter },
        new object[] { SystemRoles.CompanyAdministrator },
    };

    [Theory]
    [MemberData(nameof(ForbiddenRoles))]
    public async Task Returns_Forbidden_For_NonHrAdministrator_Roles(Guid roleId)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), roleId);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Ok_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── behaviour ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Empty_State_Reports_NoActionRequired()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var payload = await client.GetFromJsonAsync<CompliancePayload>(Url(companyId));

        Assert.NotNull(payload);
        Assert.True(payload!.NoActionRequired);
        Assert.Empty(payload.Items);
        Assert.Equal(0, payload.TotalCount);
        Assert.Equal(0, payload.Summary.Total);
    }

    [Fact]
    public async Task Company_Isolation_HrAdmin_Sees_Only_Own_Company_Rows()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();

        await SeedExpiringDocumentAsync(companyA, employeeA, "Work Visa", Today.AddDays(10));
        await SeedDocumentRequestAsync(companyA, employeeA, "Passport", Today.AddDays(5));
        await SeedPendingProbationReviewAsync(companyA, employeeA, Today.AddDays(7));

        await SeedExpiringDocumentAsync(companyB, employeeB, "Work Visa", Today.AddDays(10));
        await SeedDocumentRequestAsync(companyB, employeeB, "Passport", Today.AddDays(5));
        await SeedPendingProbationReviewAsync(companyB, employeeB, Today.AddDays(7));

        using var client = await ClientFor(companyA, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<CompliancePayload>(Url(companyA));

        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Items);
        Assert.All(payload.Items, i => Assert.NotEqual(employeeB, i.EmployeeId));
        Assert.Equal(3, payload.Summary.Total);
        Assert.Equal(3, payload.TotalCount);
    }

    [Fact]
    public async Task Date_Boundary_EndToEnd_Classifies_Overdue_And_DueSoon()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await SeedExpiringDocumentAsync(companyId, employeeId, "Work Visa", Today.AddDays(-1)); // Overdue
        await SeedExpiringDocumentAsync(companyId, employeeId, "Passport", Today.AddDays(10));   // DueSoon

        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<CompliancePayload>(Url(companyId));

        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Summary.Total);
        Assert.Equal(1, payload.Summary.Overdue);
        Assert.Equal(1, payload.Summary.DueSoon);
        Assert.Equal("Overdue", payload.Items[0].Severity); // overdue sorts first
    }

    [Fact]
    public async Task Filters_By_Category_ExpiringVisa()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await SeedExpiringDocumentAsync(companyId, employeeId, "Work Visa", Today.AddDays(10));
        await SeedExpiringDocumentAsync(companyId, employeeId, "First Aid Certificate", Today.AddDays(10));

        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<CompliancePayload>(Url(companyId) + "?category=ExpiringVisa");

        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal("ExpiringVisa", item.Category);
    }

    [Fact]
    public async Task Returns_BadRequest_For_Invalid_Severity()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var response = await client.GetAsync(Url(companyId) + "?severity=bogus");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── seeding helpers ────────────────────────────────────────────────────

    private async Task SeedExpiringDocumentAsync(
        Guid companyId, Guid employeeId, string documentTypeName, DateOnly expiryDate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        var docType = DocumentType.Create(Guid.NewGuid(), companyId, documentTypeName, null, Now);
        db.DocumentTypes.Add(docType);
        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, $"{documentTypeName} scan", null, docType.Id,
            "file.pdf", 1024, "application/pdf", $"storage/{Guid.NewGuid():N}/file.pdf", null, Guid.NewGuid(), Now);
        db.Documents.Add(doc);
        db.EmployeeDocuments.Add(EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeId, doc.Id, Guid.NewGuid(), Now, expiryDate: expiryDate));
        await db.SaveChangesAsync();
    }

    private async Task SeedDocumentRequestAsync(
        Guid companyId, Guid employeeId, string documentTypeName, DateOnly? dueDate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();

        var docType = DocumentType.Create(Guid.NewGuid(), companyId, documentTypeName, null, Now);
        db.DocumentTypes.Add(docType);
        db.DocumentRequests.Add(DocumentRequest.Create(
            Guid.NewGuid(), companyId, employeeId, docType.Id,
            positionProfileRequiredDocumentId: null, dueDate: dueDate,
            isMandatory: true, notes: null, requestedByEmployeeId: null, Now));
        await db.SaveChangesAsync();
    }

    private async Task SeedPendingProbationReviewAsync(Guid companyId, Guid employeeId, DateOnly dueDate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbationDbContext>();

        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1), null, Today, Now);
        db.ProbationRecords.Add(record);
        db.ProbationReviews.Add(ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.ManagerCheckIn, dueDate, Now));
        await db.SaveChangesAsync();
    }

    private sealed record CompliancePayload(
        List<ComplianceItemPayload> Items,
        List<ComplianceCategorySummaryPayload> CategorySummaries,
        ComplianceSummaryPayload Summary,
        int TotalCount,
        bool IsTruncated,
        bool NoActionRequired);

    private sealed record ComplianceItemPayload(
        Guid EmployeeId, string EmployeeName, string? Department, string Category,
        string CategoryLabel, string Detail, DateOnly? DueDate, string Severity, string DeepLinkUrl);

    private sealed record ComplianceCategorySummaryPayload(
        string Category, string CategoryLabel, int Total, int Overdue, int DueSoon, int Informational);

    private sealed record ComplianceSummaryPayload(int Total, int Overdue, int DueSoon, int Informational);
}
