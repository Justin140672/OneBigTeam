using System.Net;
using System.Text;
using HR.SharedKernel.Http;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

/// <summary>
/// Ticket 3 (P1): NotificationService, SicknessService, ProfilePhotoService and DataImportService must
/// never represent an API failure as legitimate empty data. Every operation is exercised against the same
/// failure matrix (401/403/404/409/422/500/network/malformed/timeout/cancellation).
/// </summary>
public class ApiFailureContractTests
{
    public sealed record Outcome(bool Success, ApiFailureKind Kind);

    private static async Task<Outcome> Run<T>(Task<ApiResult<T>> call)
    {
        var r = await call;
        return new Outcome(r.Success, r.FailureKind);
    }

    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid E = Guid.NewGuid();

    // ---- operations under test (name, invoker) ----
    private static readonly (string Name, Func<HrApiHttpClientFactory, CancellationToken, Task<Outcome>> Call)[] Operations =
    [
        ("Notifications.Get", (f, ct) => Run(new NotificationService(f).GetAsync(C, 1, 20, ct))),
        ("Notifications.UnreadCount", (f, ct) => Run(new NotificationService(f).GetUnreadCountAsync(C, ct))),
        ("Notifications.MarkRead", (f, ct) => Run(new NotificationService(f).MarkReadAsync(C, E, ct))),
        ("Notifications.MarkAllRead", (f, ct) => Run(new NotificationService(f).MarkAllReadAsync(C, E, ct))),
        ("Sickness.ReturnToWorkReview", (f, ct) => Run(new SicknessService(f).GetReturnToWorkReviewAsync(C, E, ct))),
        ("Sickness.ListEmployee", (f, ct) => Run(new SicknessService(f).ListEmployeeSicknessRecordsAsync(C, E, ct))),
        ("Sickness.GetMine", (f, ct) => Run(new SicknessService(f).GetMySicknessRecordsAsync(C, E, ct))),
        ("Sickness.Record", (f, ct) => Run(new SicknessService(f).RecordSicknessAsync(C, E, Record, ct))),
        ("Sickness.RecordMine", (f, ct) => Run(new SicknessService(f).RecordMySicknessAsync(C, E, Record, ct))),
        ("Sickness.Close", (f, ct) => Run(new SicknessService(f).CloseSicknessRecordAsync(C, E, E, new CloseSicknessRecordRequest(DateOnly.FromDateTime(DateTime.UtcNow), "FullDay", null, null), ct))),
        ("Sickness.Current", (f, ct) => Run(new SicknessService(f).GetCurrentSicknessAbsencesAsync(C, ct))),
        ("Sickness.TeamToday", (f, ct) => Run(new SicknessService(f).GetTeamSicknessTodayAsync(C, E, ct))),
        ("Sickness.MissingFitNotes", (f, ct) => Run(new SicknessService(f).GetMissingFitNotesAsync(C, ct))),
        ("Photo.GetMine", (f, ct) => Run(new ProfilePhotoService(f).GetMyProfilePhotoAsync(C, ct))),
        ("Photo.CancelPending", (f, ct) => Run(new ProfilePhotoService(f).CancelPendingProfilePhotoAsync(C, ct))),
        ("Photo.GetPending", (f, ct) => Run(new ProfilePhotoService(f).GetPendingProfilePhotoAsync(C, E, ct))),
        ("Photo.GetPendingById", (f, ct) => Run(new ProfilePhotoService(f).GetPendingProfilePhotoByIdAsync(C, E, ct))),
        ("Photo.GetEmployee", (f, ct) => Run(new ProfilePhotoService(f).GetEmployeeProfilePhotoAsync(C, E, ct))),
        ("Photo.Approve", (f, ct) => Run(new ProfilePhotoService(f).ApproveProfilePhotoAsync(C, E, ct))),
        ("Photo.Reject", (f, ct) => Run(new ProfilePhotoService(f).RejectProfilePhotoAsync(C, E, "no", ct))),
        ("Photo.UploadMine", (f, ct) => Run(new ProfilePhotoService(f).UploadMyProfilePhotoAsync(C, new FakeFile(), ct))),
        ("Photo.UploadEmployee", (f, ct) => Run(new ProfilePhotoService(f).UploadEmployeeProfilePhotoAsync(C, E, new FakeFile(), ct))),
        ("Import.Upload", (f, ct) => Run(new DataImportService(f).UploadFileAsync(C, new FakeFile(), ct))),
        ("Import.Validate", (f, ct) => Run(new DataImportService(f).ValidateSessionAsync(C, E, null, ct))),
        ("Import.Preview", (f, ct) => Run(new DataImportService(f).GetPreviewAsync(C, E, ct))),
        ("Import.Confirm", (f, ct) => Run(new DataImportService(f).ConfirmSessionAsync(C, E, ct))),
        ("Import.Columns", (f, ct) => Run(new DataImportService(f).GetSessionColumnsAsync(C, E, ct))),
        ("Import.List", (f, ct) => Run(new DataImportService(f).ListSessionsAsync(C, ct))),
        ("Import.Get", (f, ct) => Run(new DataImportService(f).GetSessionAsync(C, E, ct))),
        ("Import.DownloadErrors", (f, ct) => Run(new DataImportService(f).DownloadErrorReportAsync(C, E, ct))),
        ("Import.DownloadTemplate", (f, ct) => Run(new DataImportService(f).DownloadTemplateAsync(C, ct))),
    ];

    private static readonly RecordSicknessRequest Record =
        new(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), "FullDay", null, null, null);

    private sealed class FakeFile(long size = 4, bool tooLarge = false) : IBrowserFile
    {
        public string Name => "photo.png";
        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
        public long Size => size;
        public string ContentType => "image/png";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            tooLarge
                ? throw new IOException("Supplied file with size exceeds the maximum.")
                : new MemoryStream(Encoding.UTF8.GetBytes("test"));
    }

    private sealed class CancellationAwareHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("The request timed out.");
    }

    private sealed class EmptyOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("", Encoding.UTF8, "application/json"),
            });
    }

    public static IEnumerable<object[]> OperationNames() => Operations.Select(o => new object[] { o.Name });

    private static Func<HrApiHttpClientFactory, CancellationToken, Task<Outcome>> Op(string name) =>
        Operations.Single(o => o.Name == name).Call;

    private static bool IsFileOrMutation(string name) =>
        name is "Notifications.MarkRead" or "Notifications.MarkAllRead" or "Sickness.Record" or "Sickness.RecordMine"
            or "Sickness.Close" or "Photo.CancelPending" or "Photo.Approve" or "Photo.Reject" or "Photo.UploadMine"
            or "Photo.UploadEmployee";

    // ---- failure matrix ----

    [Theory, MemberData(nameof(OperationNames))]
    public async Task Unauthorized_Is_Unauthenticated_Not_Empty(string name) =>
        Assert.Equal(new Outcome(false, ApiFailureKind.Unauthenticated),
            await Op(name)(BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null)), default));

    [Theory, MemberData(nameof(OperationNames))]
    public async Task Forbidden_Is_Forbidden_Not_Empty(string name) =>
        Assert.Equal(new Outcome(false, ApiFailureKind.Forbidden),
            await Op(name)(BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null)), default));

    [Theory, MemberData(nameof(OperationNames))]
    public async Task NotFound_Is_NotFound(string name) =>
        Assert.Equal(new Outcome(false, ApiFailureKind.NotFound),
            await Op(name)(BuildFactory(new JsonResponseHandler(HttpStatusCode.NotFound, new { error = "gone" })), default));

    [Theory, MemberData(nameof(OperationNames))]
    public async Task Conflict_Is_Conflict(string name) =>
        Assert.Equal(new Outcome(false, ApiFailureKind.Conflict),
            await Op(name)(BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "already done" })), default));

    [Theory, MemberData(nameof(OperationNames))]
    public async Task Stale_Version_Conflict_Is_Concurrency(string name) =>
        Assert.Equal(new Outcome(false, ApiFailureKind.Concurrency),
            await Op(name)(BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "changed", code = "concurrency" })), default));

    [Theory, MemberData(nameof(OperationNames))]
    public async Task Unprocessable_Is_Validation(string name) =>
        Assert.Equal(new Outcome(false, ApiFailureKind.Validation),
            await Op(name)(BuildFactory(new JsonResponseHandler(HttpStatusCode.UnprocessableEntity, new { error = "bad input" })), default));

    [Theory, MemberData(nameof(OperationNames))]
    public async Task ServerError_Is_Server_Failure(string name) =>
        Assert.Equal(new Outcome(false, ApiFailureKind.Server),
            await Op(name)(BuildFactory(new JsonResponseHandler(HttpStatusCode.InternalServerError, new { error = "boom" })), default));

    [Theory, MemberData(nameof(OperationNames))]
    public async Task HttpRequestException_Is_Network_Failure(string name) =>
        Assert.Equal(new Outcome(false, ApiFailureKind.Network),
            await Op(name)(BuildFactory(new ThrowingHandler()), default));

    [Theory, MemberData(nameof(OperationNames))]
    public async Task Timeout_Without_Caller_Cancellation_Is_Network_Failure(string name) =>
        Assert.Equal(new Outcome(false, ApiFailureKind.Network),
            await Op(name)(BuildFactory(new TimeoutHandler()), default));

    [Theory, MemberData(nameof(OperationNames))]
    public async Task Caller_Cancellation_Propagates(string name)
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Op(name)(BuildFactory(new CancellationAwareHandler()), cts.Token));
    }

    [Theory, MemberData(nameof(OperationNames))]
    public async Task Failed_Status_With_Malformed_Body_Is_Still_A_Failure(string name)
    {
        var outcome = await Op(name)(BuildFactory(new MalformedJsonHandler(HttpStatusCode.BadGateway)), default);

        Assert.False(outcome.Success);
        Assert.Equal(ApiFailureKind.Server, outcome.Kind);
    }

    [Theory, MemberData(nameof(OperationNames))]
    public async Task Ok_With_Malformed_Json_Is_InvalidResponse_For_Reads_And_Success_For_NoContent_Mutations(string name)
    {
        var outcome = await Op(name)(BuildFactory(new MalformedJsonHandler()), default);

        if (IsFileOrMutation(name) || name.StartsWith("Import.Download", StringComparison.Ordinal))
            Assert.True(outcome.Success); // body is not read for these
        else
            Assert.Equal(new Outcome(false, ApiFailureKind.InvalidResponse), outcome);
    }

    [Theory]
    [InlineData("Notifications.Get")]
    [InlineData("Notifications.UnreadCount")]
    [InlineData("Sickness.ListEmployee")]
    [InlineData("Sickness.GetMine")]
    [InlineData("Photo.GetMine")]
    [InlineData("Import.Preview")]
    [InlineData("Import.Columns")]
    [InlineData("Import.Validate")]
    [InlineData("Import.Confirm")]
    [InlineData("Import.Upload")]
    public async Task Ok_With_Empty_Json_Body_Is_InvalidResponse_Never_Empty_Data(string name) =>
        Assert.Equal(new Outcome(false, ApiFailureKind.InvalidResponse),
            await Op(name)(BuildFactory(new EmptyOkHandler()), default));

    // ---- success and success-empty ----

    [Fact]
    public async Task Notifications_Get_Success_Returns_Items_And_Success_Empty_Is_Distinct_From_Failure()
    {
        var payload = new NotificationsResponse(0, [], 0, 1, 20, 0);
        var service = new NotificationService(BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, payload)));

        var result = await service.GetAsync(C);

        Assert.True(result.Success);
        Assert.Empty(result.Value!.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task Notifications_UnreadCount_Returns_The_Count_Including_A_Genuine_Zero(int count)
    {
        var service = new NotificationService(BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, new { count })));

        var result = await service.GetUnreadCountAsync(C);

        Assert.True(result.Success);
        Assert.Equal(count, result.Value);
    }

    [Fact]
    public async Task Notifications_Mutations_Succeed_On_NoContent_And_Fail_On_Server_Error()
    {
        var ok = new NotificationService(BuildFactory(new JsonResponseHandler(HttpStatusCode.NoContent, null)));
        var bad = new NotificationService(BuildFactory(new JsonResponseHandler(HttpStatusCode.InternalServerError, null)));

        Assert.True((await ok.MarkReadAsync(C, E)).Success);
        Assert.True((await ok.MarkAllReadAsync(C, E)).Success);
        Assert.False((await bad.MarkReadAsync(C, E)).Success);
        Assert.False((await bad.MarkAllReadAsync(C, E)).Success);
    }

    [Fact]
    public async Task Sickness_List_Success_Empty_Is_A_Successful_Empty_List()
    {
        var service = new SicknessService(BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, new { records = Array.Empty<object>() })));

        var result = await service.ListEmployeeSicknessRecordsAsync(C, E);

        Assert.True(result.Success);
        Assert.Empty(result.Value!.Records);
    }

    [Fact]
    public async Task Sickness_Mutation_Surfaces_ValidationMessage_From_Api()
    {
        var service = new SicknessService(BuildFactory(new JsonResponseHandler(
            HttpStatusCode.UnprocessableEntity, new { error = "Start date must not be in the future." })));

        var result = await service.RecordSicknessAsync(C, E, Record);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Validation, result.FailureKind);
        Assert.Equal("Start date must not be in the future.", result.DisplayMessage);
    }

    [Fact]
    public async Task ProfilePhoto_Get_NotFound_Is_Distinguishable_From_Server_Failure()
    {
        var notFound = new ProfilePhotoService(BuildFactory(new JsonResponseHandler(HttpStatusCode.NotFound, null)));
        var server = new ProfilePhotoService(BuildFactory(new JsonResponseHandler(HttpStatusCode.InternalServerError, null)));

        Assert.Equal(ApiFailureKind.NotFound, (await notFound.GetEmployeeProfilePhotoAsync(C, E)).FailureKind);
        Assert.Equal(ApiFailureKind.Server, (await server.GetEmployeeProfilePhotoAsync(C, E)).FailureKind);
    }

    [Fact]
    public async Task ProfilePhoto_Upload_Success_And_Oversized_File_Is_A_Validation_Failure_Not_A_Raw_Exception()
    {
        var ok = new ProfilePhotoService(BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, new { })));
        var tooLarge = await ok.UploadMyProfilePhotoAsync(C, new FakeFile(tooLarge: true));

        Assert.True((await ok.UploadMyProfilePhotoAsync(C, new FakeFile())).Success);
        Assert.Equal(ApiFailureKind.Validation, tooLarge.FailureKind);
        Assert.DoesNotContain("Supplied file", tooLarge.DisplayMessage);
    }

    [Fact]
    public async Task DataImport_Download_Success_Returns_Bytes_And_Server_Provided_FileName()
    {
        var handler = new FileHandler(new byte[] { 1, 2, 3 }, "template-2026.xlsx");
        var service = new DataImportService(BuildFactory(handler));

        var result = await service.DownloadTemplateAsync(C);

        Assert.True(result.Success);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Value!.Bytes);
        Assert.Equal("template-2026.xlsx", result.Value.FileName);
    }

    [Fact]
    public async Task DataImport_Download_Without_ContentDisposition_Uses_Fallback_FileName_Quietly()
    {
        var logger = new CapturingLogger<DataImportService>();
        var service = new DataImportService(BuildFactory(new FileHandler(new byte[] { 9 }, null)), logger);

        var result = await service.DownloadTemplateAsync(C);

        Assert.True(result.Success);
        Assert.Equal("employee-import-template.xlsx", result.Value!.FileName);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task DataImport_GetSession_NotFound_Is_Reported_As_NotFound()
    {
        var service = new DataImportService(BuildFactory(new JsonResponseHandler(HttpStatusCode.NotFound, null)));

        Assert.Equal(ApiFailureKind.NotFound, (await service.GetSessionAsync(C, E)).FailureKind);
    }

    private sealed class FileHandler(byte[] bytes, string? fileName) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            if (fileName is not null)
                response.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = fileName };
            return Task.FromResult(response);
        }
    }

    // ---- structured logging ----

    [Fact]
    public async Task Server_And_Network_Failures_Are_Logged_Once_Without_Bodies_Or_Tokens()
    {
        var logger = new CapturingLogger<NotificationService>();
        var server = new NotificationService(
            BuildFactory(new JsonResponseHandler(HttpStatusCode.InternalServerError, new { error = "secret-detail-xyz" })), logger);
        var network = new NotificationService(BuildFactory(new ThrowingHandler()), logger);

        await server.MarkReadAsync(C, E);
        await network.GetUnreadCountAsync(C);

        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, e =>
        {
            Assert.Equal(LogLevel.Warning, e.Level);
            Assert.DoesNotContain("secret-detail-xyz", e.Message);
            Assert.DoesNotContain("Bearer", e.Message);
        });
        Assert.Contains("Notifications.MarkRead", logger.Entries[0].Message);
        Assert.Contains("Server", logger.Entries[0].Message);
        Assert.Contains("Network", logger.Entries[1].Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Expected_Client_Failures_Are_Not_Logged_As_Operational_Errors(HttpStatusCode status)
    {
        var logger = new CapturingLogger<SicknessService>();
        var service = new SicknessService(BuildFactory(new JsonResponseHandler(status, new { error = "x" })), logger);

        await service.GetMySicknessRecordsAsync(C, E);

        Assert.Empty(logger.Entries);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
