using System.Text.RegularExpressions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// CodeQL #66 (log forging via candidate document storage keys):
/// <see cref="CandidateDocumentUploadStaging.CompensateAsync"/> logs a failed compensating delete
/// with <see cref="CandidateDocumentUploadStaging.RedactStorageKey"/>. Real keys are
/// "{companyId}/{candidateId}/{server GUID}{allow-listed extension}" (ValidateFile runs before
/// GenerateStorageKey), so these tests pin (a) the redaction output shape for every real key,
/// (b) that even a hostile key can never emit a control character, (c) that the extension
/// allow-list rejects names carrying control/encoded characters, (d) that both storage
/// implementations generate keys from a server GUID + extension only, and (e) the actual
/// CompensateAsync warning's StorageKeySuffix property.
/// </summary>
public class CandidateDocumentStorageKeyLogSafetyTests
{
    private const char LineSeparator = (char)0x2028;

    private static readonly Regex RealKeyRedaction = new(@"^\*\*\*[A-Za-z0-9._-]{1,12}\z");
    private static readonly Regex HostileKeyRedaction = new(@"^\*\*\*[A-Za-z0-9._?-]{1,12}\z");

    private static readonly CandidateDocumentUploadOptions DefaultOptions = new();

    public static TheoryData<string> AllowedExtensions()
    {
        var data = new TheoryData<string>();
        foreach (var ext in new CandidateDocumentUploadOptions().AllowedExtensions)
            data.Add(ext);
        return data;
    }

    public static TheoryData<string> HostileKeys => new()
    {
        "company/candidate/x.pdf\r\nFORGED",
        "company/candidate/x\n.pdf",
        "x.pdf\u0000",
        "x" + LineSeparator + ".pdf",
        "evil\r\n",
    };

    // ── (a) redaction of real keys ────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllowedExtensions))]
    public void RedactStorageKey_Keeps_Only_Last_12_Chars_Of_Final_Segment_For_Real_Keys(string extension)
    {
        var companyId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var finalSegment = $"{Guid.NewGuid():N}{extension}";
        var key = $"{companyId}/{candidateId}/{finalSegment}";

        var redacted = CandidateDocumentUploadStaging.RedactStorageKey(key);

        Assert.Equal("***" + finalSegment[^12..], redacted);
        Assert.Matches(RealKeyRedaction, redacted);
        Assert.DoesNotContain(companyId.ToString(), redacted);
        Assert.DoesNotContain(candidateId.ToString(), redacted);
    }

    // ── (b) redaction of hostile keys ─────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(HostileKeys))]
    public void RedactStorageKey_Never_Emits_Control_Or_Line_Separator_Chars_For_Hostile_Keys(string hostileKey)
    {
        var redacted = CandidateDocumentUploadStaging.RedactStorageKey(hostileKey);

        Assert.DoesNotContain(redacted, char.IsControl);
        Assert.DoesNotContain(LineSeparator, redacted);
        Assert.Matches(HostileKeyRedaction, redacted);
    }

    // ── (c) extension allow-list ──────────────────────────────────────────────────

    [Theory]
    [InlineData("evil.pdf\r\nX")]
    [InlineData("evil.pdf\n")]
    [InlineData("evil.pd\nf")]
    [InlineData("evil.pdf\u0000")]
    [InlineData("evil.pdf%0D%0A")]
    [InlineData("evil.exe")]
    public void ValidateFile_Rejects_Names_Whose_Extension_Is_Not_Allow_Listed(string fileName)
    {
        var result = CandidateDocumentUploadStaging.ValidateFile(fileName, "application/pdf", 100, DefaultOptions);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("cv.pdf")]
    [InlineData("CV.PDF")]
    [InlineData("cover-letter.docx")]
    public void ValidateFile_Accepts_Allow_Listed_Names(string fileName)
    {
        var result = CandidateDocumentUploadStaging.ValidateFile(fileName, "application/pdf", 100, DefaultOptions);

        Assert.True(result.IsSuccess);
    }

    // ── (d) generated key shape ───────────────────────────────────────────────────

    [Fact]
    public void LocalStorage_GenerateStorageKey_Uses_Server_Guid_And_Extension_Only()
    {
        var companyId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var sut = new LocalCandidateDocumentStorageService(new Microsoft.AspNetCore.Http.HttpContextAccessor(), new HR.Infrastructure.Abstractions.LocalStorageUrlSigner(TimeProvider.System));

        var key = sut.GenerateStorageKey($"{companyId}/{candidateId}", "cv.pdf");

        Assert.Matches(ExpectedKeyShape(companyId, candidateId), key);
        Assert.DoesNotContain("cv.pdf", key);
    }

    [Fact]
    public void SupabaseStorage_GenerateStorageKey_Uses_Server_Guid_And_Extension_Only()
    {
        var companyId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        using var httpClient = new HttpClient();
        var sut = new SupabaseCandidateDocumentStorageService(
            httpClient,
            Options.Create(new SupabaseCandidateDocumentStorageOptions
            {
                SupabaseUrl = "https://example.supabase.co",
                ServiceRoleKey = "service-role-key",
                BucketName = "candidate-documents",
                SignedUrlExpirySeconds = 3600,
            }));

        var key = sut.GenerateStorageKey($"{companyId}/{candidateId}", "cv.pdf");

        Assert.Matches(ExpectedKeyShape(companyId, candidateId), key);
        Assert.DoesNotContain("cv.pdf", key);
    }

    private static Regex ExpectedKeyShape(Guid companyId, Guid candidateId) =>
        new($@"^{Regex.Escape(companyId.ToString())}/{Regex.Escape(candidateId.ToString())}/[0-9a-f]{{32}}\.pdf\z");

    // ── (e) the actual CompensateAsync warning ────────────────────────────────────

    public static TheoryData<string> CompensationKeyTails => new()
    {
        "0123456789abcdef0123456789abcdef.pdf",
        "x.pdf\r\nFORGED",
        "x" + LineSeparator + ".pdf",
    };

    [Theory]
    [MemberData(nameof(CompensationKeyTails))]
    public async Task CompensateAsync_Failed_Delete_Warning_Logs_Only_A_Safe_Redacted_Suffix(string keyTail)
    {
        var companyId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var storageKey = $"{companyId}/{candidateId}/{keyTail}";

        var storage = new FakeCandidateDocumentStorageService { ThrowOnNextDeleteAttempts = 1 };
        var logger = new CapturingLogger();
        await using var db = new RecruitmentDbContext(
            new DbContextOptionsBuilder<RecruitmentDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
        var clock = new FakeClock(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        var staging = new CandidateDocumentUploadStaging(db, storage, clock, logger);

        var intent = CandidateDocumentDeletionOperation.CreateReservedUploadIntent(
            Guid.NewGuid(), companyId, candidateId, storageKey,
            new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        var staged = new StagedCandidateDocumentUpload(
            companyId, candidateId, storageKey, "cv.pdf", 100, "application/pdf", intent);

        await staging.CompensateAsync(staged);

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        var suffix = Assert.IsType<string>(warning.RawStateValue("StorageKeySuffix"));
        Assert.Matches(HostileKeyRedaction, suffix);
        Assert.DoesNotContain(suffix, char.IsControl);
        Assert.DoesNotContain(LineSeparator, suffix);

        // The structured message/state (exception text excluded — it is the storage client's own
        // exception, not this log statement's interpolation) must never carry the full key or any
        // control character.
        Assert.DoesNotContain(warning.MessageAndStateText, char.IsControl);
        Assert.DoesNotContain(LineSeparator, warning.MessageAndStateText);
        // Printable letters from a hostile tail may survive in the <=12-char suffix; what matters is
        // that the CR/LF separating them is neutralised, so no second log line can be forged.
        Assert.DoesNotContain("\r\nFORGED", warning.MessageAndStateText);
        Assert.DoesNotContain("\nFORGED", warning.MessageAndStateText);
        Assert.DoesNotContain($"{companyId}/{candidateId}/", warning.MessageAndStateText);
    }

    private sealed record CapturedEntry(
        LogLevel Level,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> State)
    {
        public object? RawStateValue(string key) => State.FirstOrDefault(p => p.Key == key).Value;

        public string MessageAndStateText =>
            string.Join(" | ", new[] { Message }.Concat(State.Select(p => $"{p.Key}={p.Value}")));
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<CapturedEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var pairs = state is IEnumerable<KeyValuePair<string, object?>> kvps
                ? kvps.ToList()
                : [new KeyValuePair<string, object?>("(state)", state)];
            Entries.Add(new CapturedEntry(logLevel, formatter(state, exception), pairs));
        }
    }
}
