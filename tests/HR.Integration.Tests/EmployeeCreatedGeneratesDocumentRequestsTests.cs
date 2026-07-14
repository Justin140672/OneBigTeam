using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class EmployeeCreatedGeneratesDocumentRequestsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = Guid.Parse("ff000001-0000-0000-0000-000000000001");

    public EmployeeCreatedGeneratesDocumentRequestsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    // ── DocumentRequest creation ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateEmployee_WithPositionProfile_GeneratesDocumentRequests_For_Each_RequiredDocument()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId  = await CreatePositionProfileAsync(client, companyId, "Software Engineer");
        var docTypeId1 = await CreateDocumentTypeAsync(client, companyId, "Passport");
        var docTypeId2 = await CreateDocumentTypeAsync(client, companyId, "Right To Work");

        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeId1, isMandatory: true,  dueDays: 30);
        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeId2, isMandatory: false, dueDays: null);

        var employeeId = await CreateEmployeeAsync(client, companyId, profileId);

        var requests = await GetDocumentRequestsAsync(employeeId);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, r => Assert.Equal(employeeId, r.EmployeeId));
        Assert.All(requests, r => Assert.Equal(companyId, r.CompanyId));
        Assert.All(requests, r => Assert.Equal(DocumentRequestStatus.Requested, r.Status));
        Assert.Contains(requests, r => r.DocumentTypeId == docTypeId1);
        Assert.Contains(requests, r => r.DocumentTypeId == docTypeId2);
    }

    [Fact]
    public async Task CreateEmployee_WithPositionProfile_Sets_DueDate_From_DueDaysAfterStart()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "HR Manager");
        var docTypeId = await CreateDocumentTypeAsync(client, companyId, "Contract");
        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeId, isMandatory: true, dueDays: 14);

        var employeeId = await CreateEmployeeAsync(client, companyId, profileId);

        var request = Assert.Single(await GetDocumentRequestsAsync(employeeId));
        Assert.Equal(new DateOnly(2026, 7, 1).AddDays(14), request.DueDate);
    }

    [Fact]
    public async Task CreateEmployee_WithPositionProfile_Sets_DueDate_Null_When_DueDaysAfterStart_Not_Set()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Account Executive");
        var docTypeId = await CreateDocumentTypeAsync(client, companyId, "Driving Licence");
        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeId, isMandatory: false, dueDays: null);

        var employeeId = await CreateEmployeeAsync(client, companyId, profileId);

        var request = Assert.Single(await GetDocumentRequestsAsync(employeeId));
        Assert.Null(request.DueDate);
    }

    [Fact]
    public async Task CreateEmployee_WithPositionProfile_Sets_PositionProfileRequiredDocumentId()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Designer");
        var docTypeId = await CreateDocumentTypeAsync(client, companyId, "Portfolio");
        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeId, isMandatory: true, dueDays: null);

        var employeeId = await CreateEmployeeAsync(client, companyId, profileId);

        var request = Assert.Single(await GetDocumentRequestsAsync(employeeId));
        Assert.NotNull(request.PositionProfileRequiredDocumentId);
    }

    [Fact]
    public async Task CreateEmployee_WithoutPositionProfile_CreatesNoDocumentRequests()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId, positionProfileId: null);

        Assert.Empty(await GetDocumentRequestsAsync(employeeId));
    }

    [Fact]
    public async Task CreateEmployee_WithPositionProfile_ThatHasNoRequiredDocuments_CreatesNoRequests()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId  = await CreatePositionProfileAsync(client, companyId, "Finance Manager");
        var employeeId = await CreateEmployeeAsync(client, companyId, profileId);

        Assert.Empty(await GetDocumentRequestsAsync(employeeId));
    }

    [Fact]
    public async Task CreateEmployee_WithPositionProfile_DoesNotDuplicateRequests_For_Different_Employees()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Sales Manager");
        var docTypeId = await CreateDocumentTypeAsync(client, companyId, "Certificate");
        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeId, isMandatory: true, dueDays: null);

        var emp1 = await CreateEmployeeAsync(client, companyId, profileId);
        var emp2 = await CreateEmployeeAsync(client, companyId, profileId);

        Assert.Single(await GetDocumentRequestsAsync(emp1));
        Assert.Single(await GetDocumentRequestsAsync(emp2));
    }

    [Fact]
    public async Task CreateEmployee_Returns_Created_Regardless_Of_DocumentRequest_Outcome()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            BuildEmployeeBody(companyId, "Doc", "Test", refData.PositionProfileId, refData));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── Upload task creation ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateEmployee_WithPositionProfile_Creates_One_Upload_Task_Per_DocumentRequest()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId  = await CreatePositionProfileAsync(client, companyId, "Engineer");
        var docTypeId1 = await CreateDocumentTypeAsync(client, companyId, "Passport");
        var docTypeId2 = await CreateDocumentTypeAsync(client, companyId, "Right To Work");
        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeId1, isMandatory: true,  dueDays: 30);
        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeId2, isMandatory: false, dueDays: null);

        var employeeId = await CreateEmployeeAsync(client, companyId, profileId);

        var tasks = await GetEmployeeTasksAsync(client, companyId, employeeId);
        var uploadTasks = tasks.Where(t => t.Source == "Document").ToList();
        Assert.Equal(2, uploadTasks.Count);
    }

    [Fact]
    public async Task CreateEmployee_WithPositionProfile_Upload_Tasks_Are_Assigned_To_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Analyst");
        var docTypeId = await CreateDocumentTypeAsync(client, companyId, "Passport");
        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeId, isMandatory: true, dueDays: null);

        var employeeId = await CreateEmployeeAsync(client, companyId, profileId);

        var tasks      = await GetEmployeeTasksAsync(client, companyId, employeeId);
        var uploadTask = Assert.Single(tasks.Where(t => t.Source == "Document"));
        Assert.Equal(employeeId, uploadTask.AssignedEmployeeId);
    }

    [Fact]
    public async Task CreateEmployee_WithPositionProfile_Upload_Task_ActionType_Is_Upload()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Coordinator");
        var docTypeId = await CreateDocumentTypeAsync(client, companyId, "Certificate");
        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeId, isMandatory: true, dueDays: null);

        var employeeId = await CreateEmployeeAsync(client, companyId, profileId);

        var tasks      = await GetEmployeeTasksAsync(client, companyId, employeeId);
        var uploadTask = Assert.Single(tasks.Where(t => t.Source == "Document"));
        Assert.Equal("Upload", uploadTask.ActionType);
    }

    [Fact]
    public async Task CreateEmployee_WithPositionProfile_Upload_Task_DueDate_Matches_DocumentRequest()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var profileId = await CreatePositionProfileAsync(client, companyId, "Developer");
        var docTypeId = await CreateDocumentTypeAsync(client, companyId, "Contract");
        await AddRequiredDocumentAsync(client, companyId, profileId, docTypeId, isMandatory: true, dueDays: 30);

        var employeeId = await CreateEmployeeAsync(client, companyId, profileId);

        var tasks      = await GetEmployeeTasksAsync(client, companyId, employeeId);
        var uploadTask = Assert.Single(tasks.Where(t => t.Source == "Document"));
        Assert.Equal(new DateOnly(2026, 7, 1).AddDays(30), uploadTask.DueDate);
    }

    [Fact]
    public async Task CreateEmployee_WithoutPositionProfile_Creates_No_Upload_Tasks()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId, positionProfileId: null);

        var tasks = await GetEmployeeTasksAsync(client, companyId, employeeId);
        Assert.Empty(tasks.Where(t => t.Source == "Document"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<Guid> CreatePositionProfileAsync(HttpClient client, Guid companyId, string title)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, title });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task<Guid> CreateDocumentTypeAsync(HttpClient client, Guid companyId, string name)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-types",
            new { companyId, name = $"{name} {Guid.NewGuid():N}" });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private async Task AddRequiredDocumentAsync(
        HttpClient client, Guid companyId, Guid profileId, Guid docTypeId,
        bool isMandatory, int? dueDays)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{profileId}/required-documents",
            new { companyId, positionProfileId = profileId, documentTypeId = docTypeId, isMandatory, dueDaysAfterStart = dueDays });
        resp.EnsureSuccessStatusCode();
    }

    // Department/Location/EmploymentType/EmployeeNumber are all mandatory on employee creation —
    // seed fresh reference data per call. A null positionProfileId means "use a fresh, bare
    // position profile with no required documents of its own" (no employee can be created
    // without a real PositionProfileId any more, so a true "without position profile" scenario is
    // no longer reachable via the API — a profile with zero required documents is the closest
    // equivalent and produces the same zero-DocumentRequests outcome these tests assert on).
    private async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId, Guid? positionProfileId = null)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var effectiveProfileId = positionProfileId ?? refData.PositionProfileId;

        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            BuildEmployeeBody(companyId, "Doc", $"Employee{Guid.NewGuid():N}", effectiveProfileId, refData));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<EmpPayload>())!.Id;
    }

    private static object BuildEmployeeBody(
        Guid companyId, string firstName, string lastName, Guid positionProfileId,
        EmployeeReferenceDataSeeder.ReferenceData refData) =>
        new
        {
            companyId,
            firstName,
            lastName,
            workEmail       = $"{firstName.ToLower()}.{lastName.ToLower()}@doctest.example",
            startDate       = "2026-07-01",
            dateOfBirth     = "1990-01-01",
            nationality     = "British",
            gender          = "Male",
            employeeNumber  = $"EMP-{Guid.NewGuid():N}",
            departmentId    = refData.DepartmentId,
            locationId      = refData.LocationId,
            employmentTypeId = refData.EmploymentTypeId,
            positionProfileId,
        };

    private async Task<List<DocumentRequest>> GetDocumentRequestsAsync(Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        return await db.DocumentRequests
            .Where(r => r.EmployeeId == employeeId)
            .ToListAsync();
    }

    private async Task<List<TaskItem>> GetEmployeeTasksAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var resp = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/tasks");
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<TaskListPayload>();
        return payload!.Items.ToList();
    }

    private sealed record IdPayload(Guid Id);
    private sealed record EmpPayload(Guid Id);
    private sealed record TaskListPayload(IReadOnlyList<TaskItem> Items);
    private sealed record TaskItem(
        Guid Id,
        string Title,
        string Source,
        string ActionType,
        DateOnly? DueDate,
        Guid? AssignedEmployeeId);
}
