using System.Net.Sockets;
using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// [P1] Malware-scan lifecycle of <see cref="CandidateDocument"/>: Pending -> Scanning -> Clean |
/// Infected | (failed attempt -> Pending with back-off) | Failed. Only Clean is ever downloadable.
/// </summary>
public class CandidateDocumentScanStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    private static CandidateDocument NewDocument(DateTimeOffset? createdAt = null) =>
        CandidateDocument.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "CV", "cv.pdf", 1024, "application/pdf",
            "company/candidate/cv.pdf", Guid.NewGuid(), createdAt ?? Now, CandidateDocumentKind.Cv);

    /// <summary>Drives a document through <paramref name="failedAttempts"/> failed attempts, each
    /// immediately eligible for the next (nextAttemptAt = attempt time).</summary>
    private static DateTimeOffset FailAttempts(CandidateDocument document, int failedAttempts, DateTimeOffset start)
    {
        var at = start;
        for (var i = 0; i < failedAttempts; i++)
        {
            document.BeginScanAttempt(at);
            document.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.ScannerUnavailable, at, at);
            at = at.AddMinutes(1);
        }
        return at;
    }

    // ── Initial state ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_Starts_Pending_With_No_Attempts_And_Is_Not_Downloadable()
    {
        var document = NewDocument();

        Assert.Equal(CandidateDocumentScanStatus.Pending, document.ScanStatus);
        Assert.Equal(0, document.ScanAttemptCount);
        Assert.Null(document.ScanLastAttemptAt);
        Assert.Null(document.ScanNextAttemptAt);
        Assert.Null(document.ScanCompletedAt);
        Assert.Null(document.ScanFailureReason);
        Assert.False(document.IsDownloadable);
        Assert.False(document.IsScanTerminal);
        Assert.True(document.HasRemainingScanAttempts);
        Assert.True(document.CanBeginScanAttempt(Now));
    }

    // ── Claim ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BeginScanAttempt_Moves_To_Scanning_Increments_Attempts_And_Is_Not_Downloadable()
    {
        var document = NewDocument();

        document.BeginScanAttempt(Now);

        Assert.Equal(CandidateDocumentScanStatus.Scanning, document.ScanStatus);
        Assert.Equal(1, document.ScanAttemptCount);
        Assert.Equal(Now, document.ScanLastAttemptAt);
        Assert.Null(document.ScanNextAttemptAt);
        Assert.False(document.IsDownloadable);
        Assert.False(document.IsScanTerminal);
    }

    [Fact]
    public void BeginScanAttempt_Throws_While_A_Live_Claim_Is_Held()
    {
        var document = NewDocument();
        document.BeginScanAttempt(Now);

        Assert.False(document.CanBeginScanAttempt(Now.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => document.BeginScanAttempt(Now.AddMinutes(1)));
        Assert.Equal(1, document.ScanAttemptCount);
    }

    [Theory]
    [InlineData(nameof(CandidateDocumentScanStatus.Clean))]
    [InlineData(nameof(CandidateDocumentScanStatus.Infected))]
    [InlineData(nameof(CandidateDocumentScanStatus.Failed))]
    public void BeginScanAttempt_Throws_Once_Terminal(string terminalName)
    {
        var terminal = Enum.Parse<CandidateDocumentScanStatus>(terminalName);
        var document = NewDocument();
        DriveTo(document, terminal);
        var attempts = document.ScanAttemptCount;

        Assert.True(document.IsScanTerminal);
        Assert.False(document.CanBeginScanAttempt(Now.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => document.BeginScanAttempt(Now.AddDays(1)));
        Assert.Equal(terminal, document.ScanStatus);
        Assert.Equal(attempts, document.ScanAttemptCount);
    }

    // ── Clean ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MarkScanClean_Makes_Document_Downloadable_And_Terminal()
    {
        var document = NewDocument();
        document.BeginScanAttempt(Now);

        document.MarkScanClean(Now.AddSeconds(5));

        Assert.Equal(CandidateDocumentScanStatus.Clean, document.ScanStatus);
        Assert.True(document.IsDownloadable);
        Assert.True(document.IsScanTerminal);
        Assert.Equal(Now.AddSeconds(5), document.ScanCompletedAt);
        Assert.Null(document.ScanFailureReason);
        Assert.Null(document.ScanNextAttemptAt);
    }

    [Fact]
    public void MarkScanClean_After_A_Failed_Attempt_Clears_The_Failure_Reason()
    {
        var document = NewDocument();
        var at = FailAttempts(document, 1, Now);
        Assert.Equal(CandidateDocumentScanFailureReasons.ScannerUnavailable, document.ScanFailureReason);

        document.BeginScanAttempt(at);
        document.MarkScanClean(at);

        Assert.Null(document.ScanFailureReason);
        Assert.Equal(2, document.ScanAttemptCount);
        Assert.True(document.IsDownloadable);
    }

    // ── Infected ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MarkScanInfected_Stores_Sanitised_Threat_Name_And_Is_Not_Downloadable()
    {
        var document = NewDocument();
        document.BeginScanAttempt(Now);

        document.MarkScanInfected("  Eicar-Test-Signature  ", Now);

        Assert.Equal(CandidateDocumentScanStatus.Infected, document.ScanStatus);
        Assert.Equal("Eicar-Test-Signature", document.ScanFailureReason);
        Assert.False(document.IsDownloadable);
        Assert.True(document.IsScanTerminal);
        Assert.Equal(Now, document.ScanCompletedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Win.Trojan; DROP TABLE candidate_documents")]
    [InlineData("https://evil.example/?token=abc&x=1")]
    [InlineData("line1\nline2")]
    public void MarkScanInfected_Replaces_Unsafe_Or_Missing_Threat_Name_With_Unknown_Threat(string? threatName)
    {
        var document = NewDocument();
        document.BeginScanAttempt(Now);

        document.MarkScanInfected(threatName, Now);

        Assert.Equal(CandidateDocumentScanStatus.Infected, document.ScanStatus);
        Assert.Equal(CandidateDocumentScanFailureReasons.UnknownThreat, document.ScanFailureReason);
    }

    [Fact]
    public void SanitiseThreatName_Accepts_Exactly_150_Characters()
    {
        var name = new string('A', 150);

        Assert.Equal(name, CandidateDocumentScanFailureReasons.SanitiseThreatName(name));
    }

    [Fact]
    public void SanitiseThreatName_Rejects_151_Characters()
    {
        var name = new string('A', 151);

        Assert.Equal(CandidateDocumentScanFailureReasons.UnknownThreat, CandidateDocumentScanFailureReasons.SanitiseThreatName(name));
    }

    [Theory]
    [InlineData("Eicar-Test-Signature")]
    [InlineData("Win.Trojan.Agent-123")]
    [InlineData("Heuristics.Encrypted.PDF")]
    [InlineData("PUA.Win/Packer:UPX+1 test")]
    public void SanitiseThreatName_Keeps_Conventional_Scanner_Names(string name)
    {
        Assert.Equal(name, CandidateDocumentScanFailureReasons.SanitiseThreatName(name));
    }

    // ── Failed attempt / retry ────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordFailedScanAttempt_Returns_To_Pending_With_Next_Attempt_And_Is_Not_Downloadable()
    {
        var document = NewDocument();
        document.BeginScanAttempt(Now);
        var next = Now.AddMinutes(1);

        document.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.ScannerUnavailable, Now, next);

        Assert.Equal(CandidateDocumentScanStatus.Pending, document.ScanStatus);
        Assert.Equal(next, document.ScanNextAttemptAt);
        Assert.Equal(CandidateDocumentScanFailureReasons.ScannerUnavailable, document.ScanFailureReason);
        Assert.Null(document.ScanCompletedAt);
        Assert.False(document.IsDownloadable);
        Assert.False(document.IsScanTerminal);
        Assert.True(document.HasRemainingScanAttempts);
    }

    [Fact]
    public void Fourth_Failed_Attempt_Still_Returns_To_Pending()
    {
        var document = NewDocument();

        FailAttempts(document, CandidateDocument.MaxScanAttempts - 1, Now);

        Assert.Equal(CandidateDocumentScanStatus.Pending, document.ScanStatus);
        Assert.Equal(CandidateDocument.MaxScanAttempts - 1, document.ScanAttemptCount);
        Assert.True(document.HasRemainingScanAttempts);
    }

    [Fact]
    public void Fifth_Failed_Attempt_Is_Terminally_Failed_And_Never_Clean()
    {
        var document = NewDocument();
        var at = FailAttempts(document, CandidateDocument.MaxScanAttempts - 1, Now);

        document.BeginScanAttempt(at);
        document.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.ScanTimedOut, at, at.AddHours(1));

        Assert.Equal(5, CandidateDocument.MaxScanAttempts);
        Assert.Equal(CandidateDocumentScanStatus.Failed, document.ScanStatus);
        Assert.Equal(CandidateDocument.MaxScanAttempts, document.ScanAttemptCount);
        Assert.Null(document.ScanNextAttemptAt);
        Assert.Equal(at, document.ScanCompletedAt);
        Assert.Equal(CandidateDocumentScanFailureReasons.ScanTimedOut, document.ScanFailureReason);
        Assert.False(document.IsDownloadable);
        Assert.True(document.IsScanTerminal);
        Assert.False(document.HasRemainingScanAttempts);
    }

    // ── Results require an active claim ───────────────────────────────────────────────────────

    [Fact]
    public void Results_Cannot_Be_Recorded_While_Pending()
    {
        var document = NewDocument();

        Assert.Throws<InvalidOperationException>(() => document.MarkScanClean(Now));
        Assert.Throws<InvalidOperationException>(() => document.MarkScanInfected("Eicar-Test-Signature", Now));
        Assert.Throws<InvalidOperationException>(() =>
            document.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.GenericFailure, Now, Now));

        Assert.Equal(CandidateDocumentScanStatus.Pending, document.ScanStatus);
        Assert.False(document.IsDownloadable);
    }

    [Theory]
    [InlineData(nameof(CandidateDocumentScanStatus.Clean))]
    [InlineData(nameof(CandidateDocumentScanStatus.Infected))]
    [InlineData(nameof(CandidateDocumentScanStatus.Failed))]
    public void Results_Cannot_Be_Recorded_Twice_Once_Terminal(string terminalName)
    {
        var terminal = Enum.Parse<CandidateDocumentScanStatus>(terminalName);
        var document = NewDocument();
        DriveTo(document, terminal);

        Assert.Throws<InvalidOperationException>(() => document.MarkScanClean(Now.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => document.MarkScanInfected("Eicar-Test-Signature", Now.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() =>
            document.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.GenericFailure, Now.AddDays(1), Now.AddDays(1)));

        Assert.Equal(terminal, document.ScanStatus);
    }

    [Fact]
    public void An_Infected_Document_Can_Never_Be_Flipped_To_Clean()
    {
        var document = NewDocument();
        DriveTo(document, CandidateDocumentScanStatus.Infected);

        Assert.Throws<InvalidOperationException>(() => document.MarkScanClean(Now.AddDays(1)));
        Assert.False(document.IsDownloadable);
    }

    // ── Back-off ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CanBeginScanAttempt_Is_False_Before_Next_Attempt_And_True_At_Exactly_Next_Attempt()
    {
        var document = NewDocument();
        document.BeginScanAttempt(Now);
        var next = Now.AddMinutes(5);
        document.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.ScannerUnavailable, Now, next);

        Assert.False(document.CanBeginScanAttempt(next.AddTicks(-1)));
        Assert.Throws<InvalidOperationException>(() => document.BeginScanAttempt(next.AddTicks(-1)));
        Assert.True(document.CanBeginScanAttempt(next)); // inclusive boundary
        Assert.True(document.CanBeginScanAttempt(next.AddMinutes(1)));
    }

    // ── Lease / abandoned claim ───────────────────────────────────────────────────────────────

    [Fact]
    public void IsScanLeaseExpired_Is_False_Just_Before_Lease_And_True_At_Exactly_Lease_Duration()
    {
        var document = NewDocument();
        document.BeginScanAttempt(Now);

        Assert.Equal(TimeSpan.FromMinutes(15), CandidateDocument.ScanLeaseDuration);
        Assert.False(document.IsScanLeaseExpired(Now + CandidateDocument.ScanLeaseDuration - TimeSpan.FromTicks(1)));
        Assert.True(document.IsScanLeaseExpired(Now + CandidateDocument.ScanLeaseDuration));
        Assert.False(document.CanBeginScanAttempt(Now + CandidateDocument.ScanLeaseDuration - TimeSpan.FromTicks(1)));
        Assert.True(document.CanBeginScanAttempt(Now + CandidateDocument.ScanLeaseDuration));
    }

    [Fact]
    public void IsScanLeaseExpired_Is_False_When_Not_Scanning()
    {
        var document = NewDocument();

        Assert.False(document.IsScanLeaseExpired(Now.AddDays(10)));
    }

    [Fact]
    public void ReleaseAbandonedScan_Returns_Expired_Claim_To_Pending_And_Immediately_Due()
    {
        var document = NewDocument();
        document.BeginScanAttempt(Now);
        var releaseAt = Now + CandidateDocument.ScanLeaseDuration;

        document.ReleaseAbandonedScan(releaseAt);

        Assert.Equal(CandidateDocumentScanStatus.Pending, document.ScanStatus);
        Assert.Equal(releaseAt, document.ScanNextAttemptAt);
        Assert.Equal(CandidateDocumentScanFailureReasons.ScanAbandoned, document.ScanFailureReason);
        Assert.Equal(1, document.ScanAttemptCount);
        Assert.True(document.CanBeginScanAttempt(releaseAt));
        Assert.False(document.IsDownloadable);
    }

    [Fact]
    public void ReleaseAbandonedScan_Throws_For_A_Live_Claim()
    {
        var document = NewDocument();
        document.BeginScanAttempt(Now);

        Assert.Throws<InvalidOperationException>(() =>
            document.ReleaseAbandonedScan(Now + CandidateDocument.ScanLeaseDuration - TimeSpan.FromTicks(1)));
        Assert.Equal(CandidateDocumentScanStatus.Scanning, document.ScanStatus);
    }

    [Fact]
    public void ReleaseAbandonedScan_Throws_When_Not_Scanning()
    {
        var document = NewDocument();

        Assert.Throws<InvalidOperationException>(() => document.ReleaseAbandonedScan(Now.AddDays(1)));
    }

    [Fact]
    public void ReleaseAbandonedScan_On_The_Last_Attempt_Is_Terminally_Failed()
    {
        var document = NewDocument();
        var at = FailAttempts(document, CandidateDocument.MaxScanAttempts - 1, Now);
        document.BeginScanAttempt(at);

        document.ReleaseAbandonedScan(at + CandidateDocument.ScanLeaseDuration);

        Assert.Equal(CandidateDocumentScanStatus.Failed, document.ScanStatus);
        Assert.Equal(CandidateDocumentScanFailureReasons.ScanAbandoned, document.ScanFailureReason);
        Assert.False(document.IsDownloadable);
    }

    // ── Retry-limit close-off ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MarkScanRetryLimitReached_Throws_While_Attempts_Remain()
    {
        var document = NewDocument();

        Assert.Throws<InvalidOperationException>(() => document.MarkScanRetryLimitReached(Now));
        Assert.Equal(CandidateDocumentScanStatus.Pending, document.ScanStatus);
    }

    [Fact]
    public void MarkScanRetryLimitReached_Closes_Off_Exhausted_Pending_Document_As_Failed()
    {
        var document = NewDocument();
        SetAttemptCount(document, CandidateDocument.MaxScanAttempts);

        document.MarkScanRetryLimitReached(Now);

        Assert.Equal(CandidateDocumentScanStatus.Failed, document.ScanStatus);
        Assert.Equal(CandidateDocumentScanFailureReasons.RetryLimitReached, document.ScanFailureReason);
        Assert.Equal(Now, document.ScanCompletedAt);
        Assert.False(document.IsDownloadable);
    }

    [Fact]
    public void MarkScanRetryLimitReached_Preserves_An_Existing_Failure_Reason()
    {
        var document = NewDocument();
        FailAttempts(document, 1, Now);
        SetAttemptCount(document, CandidateDocument.MaxScanAttempts);

        document.MarkScanRetryLimitReached(Now.AddHours(1));

        Assert.Equal(CandidateDocumentScanFailureReasons.ScannerUnavailable, document.ScanFailureReason);
    }

    [Fact]
    public void BeginScanAttempt_Throws_When_No_Attempts_Remain()
    {
        var document = NewDocument();
        SetAttemptCount(document, CandidateDocument.MaxScanAttempts);

        Assert.True(document.CanBeginScanAttempt(Now));
        Assert.False(document.HasRemainingScanAttempts);
        Assert.Throws<InvalidOperationException>(() => document.BeginScanAttempt(Now));
        Assert.Equal(CandidateDocumentScanStatus.Pending, document.ScanStatus);
    }

    [Fact]
    public void MarkScanRetryLimitReached_Throws_For_Terminal_Document()
    {
        var document = NewDocument();
        DriveTo(document, CandidateDocumentScanStatus.Clean);
        SetAttemptCount(document, CandidateDocument.MaxScanAttempts);

        Assert.Throws<InvalidOperationException>(() => document.MarkScanRetryLimitReached(Now));
        Assert.Equal(CandidateDocumentScanStatus.Clean, document.ScanStatus);
    }

    // ── Failure reason mapping (closed set; raw exception text never persisted) ──────────────

    public static TheoryData<Exception, string> ExceptionMappings() => new()
    {
        { new SocketException((int)SocketError.ConnectionRefused), CandidateDocumentScanFailureReasons.ScannerUnavailable },
        { new IOException("disk"), CandidateDocumentScanFailureReasons.FileUnreadable },
        { new FileNotFoundException("missing", "secret/storage/key.pdf"), CandidateDocumentScanFailureReasons.FileUnreadable },
        { new HttpRequestException("503 from https://storage.example/object?token=abc"), CandidateDocumentScanFailureReasons.FileUnreadable },
        { new OperationCanceledException(), CandidateDocumentScanFailureReasons.ScanTimedOut },
        { new TaskCanceledException(), CandidateDocumentScanFailureReasons.ScanTimedOut },
        { new TimeoutException(), CandidateDocumentScanFailureReasons.ScanTimedOut },
        { new InvalidOperationException("Bearer eyJhbGciOi... for jane.doe@example.com"), CandidateDocumentScanFailureReasons.GenericFailure },
        { new Exception("boom"), CandidateDocumentScanFailureReasons.GenericFailure },
    };

    [Theory]
    [MemberData(nameof(ExceptionMappings))]
    public void FromException_Maps_To_Closed_Set_Category(Exception exception, string expected)
    {
        var reason = CandidateDocumentScanFailureReasons.FromException(exception);

        Assert.Equal(expected, reason);
        Assert.DoesNotContain(exception.Message, reason);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static void DriveTo(CandidateDocument document, CandidateDocumentScanStatus status)
    {
        switch (status)
        {
            case CandidateDocumentScanStatus.Clean:
                document.BeginScanAttempt(Now);
                document.MarkScanClean(Now);
                break;
            case CandidateDocumentScanStatus.Infected:
                document.BeginScanAttempt(Now);
                document.MarkScanInfected("Eicar-Test-Signature", Now);
                break;
            case CandidateDocumentScanStatus.Failed:
                FailAttempts(document, CandidateDocument.MaxScanAttempts, Now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        Assert.Equal(status, document.ScanStatus);
    }

    /// <summary>A Pending document with an exhausted budget cannot be reached through the public
    /// domain API (the fifth failure goes straight to Failed); it can exist in the database if a
    /// status was reset out-of-band, which is exactly what MarkScanRetryLimitReached guards.</summary>
    private static void SetAttemptCount(CandidateDocument document, int count) =>
        typeof(CandidateDocument).GetProperty(nameof(CandidateDocument.ScanAttemptCount))!.SetValue(document, count);
}
