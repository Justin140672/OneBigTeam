using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

// Ticket 19: verifies that FormText normalization is actually applied to the JSON body sent
// over the wire by the service methods that were updated, not just to the helper itself.
public class FormTextServiceNormalizationTests
{
    private static HrApiHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return new HrApiHttpClientFactory(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), new CircuitSessionState());
    }

    // ── ApplicationService.CreateApplicationAsync ──────────────────────────────

    [Fact]
    public async Task CreateApplicationAsync_Trims_Notes_In_Posted_Body()
    {
        var capturing = new BodyCapturingHandler(HttpStatusCode.OK);
        var service = new ApplicationService(BuildFactory(capturing));

        await service.CreateApplicationAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  hello notes  ");

        var body = await capturing.GetJsonBodyAsync();
        Assert.Equal("hello notes", body.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task CreateApplicationAsync_Blank_Notes_Become_Null_In_Posted_Body()
    {
        var capturing = new BodyCapturingHandler(HttpStatusCode.OK);
        var service = new ApplicationService(BuildFactory(capturing));

        await service.CreateApplicationAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "   ");

        var body = await capturing.GetJsonBodyAsync();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("notes").ValueKind);
    }

    // ── ApplicationService.RejectCandidateAsync ────────────────────────────────

    [Fact]
    public async Task RejectCandidateAsync_Trims_Rejection_Reason_In_Posted_Body()
    {
        var capturing = new BodyCapturingHandler(HttpStatusCode.OK);
        var service = new ApplicationService(BuildFactory(capturing));

        await service.RejectCandidateAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  not a fit  ");

        var body = await capturing.GetJsonBodyAsync();
        Assert.Equal("not a fit", body.GetProperty("rejectionReason").GetString());
    }

    [Fact]
    public async Task RejectCandidateAsync_Blank_Reason_Becomes_Null_In_Posted_Body()
    {
        var capturing = new BodyCapturingHandler(HttpStatusCode.OK);
        var service = new ApplicationService(BuildFactory(capturing));

        await service.RejectCandidateAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), string.Empty);

        var body = await capturing.GetJsonBodyAsync();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("rejectionReason").ValueKind);
    }

    // ── AssetService.AssignAssetAsync ──────────────────────────────────────────

    [Fact]
    public async Task AssignAssetAsync_Trims_Notes_In_Posted_Body()
    {
        var capturing = new BodyCapturingHandler(HttpStatusCode.OK);
        var service = new AssetService(BuildFactory(capturing));

        await service.AssignAssetAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  looks fine  ");

        var body = await capturing.GetJsonBodyAsync();
        Assert.Equal("looks fine", body.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task AssignAssetAsync_Blank_Notes_Become_Null_In_Posted_Body()
    {
        var capturing = new BodyCapturingHandler(HttpStatusCode.OK);
        var service = new AssetService(BuildFactory(capturing));

        await service.AssignAssetAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "\t\n");

        var body = await capturing.GetJsonBodyAsync();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("notes").ValueKind);
    }

    // ── AuthService.LoginAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_Trims_Email_But_Preserves_Password_Unchanged_In_Posted_Body()
    {
        var loginResponse = new { AccessToken = "at", RefreshToken = "rt", ExpiresIn = 3600 };
        var capturing = new BodyCapturingHandler(HttpStatusCode.OK, JsonContent.Create(loginResponse));
        var service = new AuthService(BuildFactory(capturing), NullLogger<AuthService>.Instance);

        await service.LoginAsync("  user@example.com  ", "  s3cret pw  ");

        var body = await capturing.GetJsonBodyAsync();
        Assert.Equal("user@example.com", body.GetProperty("email").GetString());
        Assert.Equal("  s3cret pw  ", body.GetProperty("password").GetString());
    }

    // ── DepartmentService (representative of the reference-data services: AssetCategory,
    // DocumentType, EmploymentType, LeavePolicy, LeaveType, Location, LocationType,
    // PublicHoliday, RecruitmentStage, SicknessCategory, Vacancy all follow this same
    // FormText.Required(name)/FormText.Optional(description) pattern) ────────────────────

    [Fact]
    public async Task DepartmentService_CreateAsync_Trims_Name_And_Normalizes_Blank_Description_To_Null()
    {
        var capturing = new BodyCapturingHandler(HttpStatusCode.OK, JsonContent.Create(new { id = Guid.NewGuid() }));
        var service = new DepartmentService(BuildFactory(capturing));

        await ((IEditService<DepartmentEditModel, Guid>)service).CreateAsync(
            Guid.NewGuid(), new DepartmentEditModel { Name = "  Engineering  ", Description = "   " });

        var body = await capturing.GetJsonBodyAsync();
        Assert.Equal("Engineering", body.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("description").ValueKind);
    }

    [Fact]
    public async Task DepartmentService_UpdateAsync_Trims_Name_And_Description()
    {
        var capturing = new BodyCapturingHandler(HttpStatusCode.OK, JsonContent.Create(new { version = 2 }));
        var service = new DepartmentService(BuildFactory(capturing));

        await service.UpdateAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            new DepartmentEditModel { Name = "  People  ", Description = "  Formerly HR  " },
            expectedVersion: 1);

        var body = await capturing.GetJsonBodyAsync();
        Assert.Equal("People", body.GetProperty("name").GetString());
        Assert.Equal("Formerly HR", body.GetProperty("description").GetString());
    }

    // ── DocumentService.ListSharedCompanyDocumentsAsync search trimming ────────────────

    [Fact]
    public async Task ListSharedCompanyDocumentsAsync_Trims_Search_Term_In_Query_String()
    {
        var capturing = new BodyCapturingHandler(HttpStatusCode.OK, JsonContent.Create(new SharedCompanyDocumentListResponse([], 0, 1, 20)));
        var service = new DocumentService(BuildFactory(capturing));

        await service.ListSharedCompanyDocumentsAsync(Guid.NewGuid(), search: "  handbook  ");

        Assert.Contains("search=handbook", capturing.CapturedRequestUri);
    }

    [Fact]
    public async Task ListSharedCompanyDocumentsAsync_Whitespace_Only_Search_Is_Omitted_From_Query_String()
    {
        var capturing = new BodyCapturingHandler(HttpStatusCode.OK, JsonContent.Create(new SharedCompanyDocumentListResponse([], 0, 1, 20)));
        var service = new DocumentService(BuildFactory(capturing));

        await service.ListSharedCompanyDocumentsAsync(Guid.NewGuid(), search: "   ");

        Assert.DoesNotContain("search=", capturing.CapturedRequestUri);
    }

    // ── DocumentService.ArchiveSharedCompanyDocumentAsync ───────────────────────────────

    [Fact]
    public async Task ArchiveSharedCompanyDocumentAsync_Trims_Reason_In_Posted_Body()
    {
        var capturing = new BodyCapturingHandler(HttpStatusCode.OK, JsonContent.Create(new { }));
        var service = new DocumentService(BuildFactory(capturing));

        await service.ArchiveSharedCompanyDocumentAsync(Guid.NewGuid(), Guid.NewGuid(), "  no longer needed  ");

        var body = await capturing.GetJsonBodyAsync();
        Assert.Equal("no longer needed", body.GetProperty("reason").GetString());
    }

    // ── CandidateService search trimming ────────────────────────────────────────────────

    [Fact]
    public async Task ListCandidatesAsync_Trims_Search_Term_In_Query_String()
    {
        var capturing = new BodyCapturingHandler(HttpStatusCode.OK, JsonContent.Create(new ListCandidatesResponse([], 0, 1, 20, 0)));
        var service = new CandidateService(BuildFactory(capturing));

        await service.ListCandidatesAsync(Guid.NewGuid(), search: "  jane  ");

        Assert.Contains("search=jane", capturing.CapturedRequestUri);
    }

    // ── SupportService.SubmitSupportRequestAsync (multipart) ───────────────────────────

    [Fact]
    public async Task SubmitSupportRequestAsync_Trims_Title_And_Description_In_Posted_Multipart_Body()
    {
        var capturing = new BodyCapturingHandler(HttpStatusCode.OK, JsonContent.Create(new SubmitSupportRequestResult(Guid.NewGuid(), "SR-1")));
        var service = new SupportService(BuildFactory(capturing), NullLogger<SupportService>.Instance);

        await service.SubmitSupportRequestAsync(
            Guid.NewGuid(), "Bug", "  Can't save  ", "  It fails every time  ", "Normal",
            includeDiagnostics: false, null, null, null, null, null, []);

        var body = capturing.CapturedRawBody;
        Assert.NotNull(body);
        Assert.Contains("Can't save", body);
        Assert.DoesNotContain("  Can't save  ", body);
        Assert.Contains("It fails every time", body);
        Assert.DoesNotContain("  It fails every time  ", body);
    }

    // ── Fake handlers ────────────────────────────────────────────────────────────

    private sealed class BodyCapturingHandler(HttpStatusCode statusCode, HttpContent? responseContent = null) : HttpMessageHandler
    {
        private string? _capturedBody;

        public string? CapturedRawBody => _capturedBody;
        public string CapturedRequestUri { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequestUri = request.RequestUri?.ToString() ?? string.Empty;
            if (request.Content is not null)
                _capturedBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode) { Content = responseContent ?? JsonContent.Create(new { }) };
        }

        public Task<JsonElement> GetJsonBodyAsync()
        {
            Assert.NotNull(_capturedBody);
            return Task.FromResult(JsonDocument.Parse(_capturedBody!).RootElement);
        }
    }
}

file sealed class NullLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public static readonly NullLogger<T> Instance = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
    }
}
