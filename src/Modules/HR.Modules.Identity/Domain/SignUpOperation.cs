using HR.SharedKernel;

namespace HR.Modules.Identity.Domain;

/// <summary>
/// Durable record of one public signup. Claimed atomically (unique idempotency key / unique active
/// email) before any provisioning side effect, advanced stage by stage as each cross-module or
/// external step commits, and finished either by storing the replayable response or by recording a
/// compensated terminal failure. Never holds the password or any token.
/// </summary>
internal sealed class SignUpOperation : IVersionedAggregate
{
    private SignUpOperation() { }

    public const string StatusInProgress = "in_progress";
    public const string StatusCompleted = "completed";
    public const string StatusFailed = "failed";

    public const string StageClaimed = "claimed";
    public const string StageCompanyProvisioned = "company_provisioned";
    public const string StageEmployeeCreated = "employee_created";
    public const string StageIdentityCreated = "identity_created";
    public const string StageCompleted = "completed";
    public const string StageCompensating = "compensating";
    public const string StageCompensated = "compensated";

    public const string ProvisioningMetadataKey = "provisioning_operation_id";

    public const string LegacyFingerprint = "legacy";

    /// <summary>Extra time after a lease expires before another worker may take it over or compensate it,
    /// so a just-expired worker cannot still be inside a legitimate external request.</summary>
    public static readonly TimeSpan TakeoverSafetyInterval = TimeSpan.FromMinutes(1);

    /// <summary>Upper bound for one external side effect; must stay below lease plus safety interval.</summary>
    public static readonly TimeSpan ExternalCallTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long a terminal (completed or failed) operation stays replayable before it is purged.
    /// Matches the shared idempotency-record retention; long-term history lives in the audit system.
    /// </summary>
    public static readonly TimeSpan Retention = HR.SharedKernel.Idempotency.DbContextIdempotencyExtensions.DefaultRetention;

    public Guid Id { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? RequestFingerprint { get; private set; }
    public string AdminEmail { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public Guid CompanyId { get; private set; }
    public Guid? EmployeeId { get; private set; }
    public Guid? SupabaseAuthUserId { get; private set; }
    public string Status { get; private set; } = StatusInProgress;
    public string Stage { get; private set; } = StageClaimed;
    public string? ResponseJson { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public string? LastError { get; private set; }
    public int AttemptCount { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public string? CompensationCode { get; private set; }
    public bool CompensationReleaseKey { get; private set; }
    public DateTimeOffset? SweptAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

    public bool IsInProgress => Status == StatusInProgress;

    public bool MatchesRequest(string fingerprint, string normalizedEmail) =>
        RequestFingerprint is null
        || RequestFingerprint == fingerprint
        || (RequestFingerprint == LegacyFingerprint && NormalizedEmail == normalizedEmail);

    public bool IsCompensating => Stage == StageCompensating;

    public bool LeaseIsActive(DateTimeOffset now) =>
        LeaseExpiresAt is { } expires && expires + TakeoverSafetyInterval > now;

    public static SignUpOperation Claim(
        string? idempotencyKey, string fingerprint, string adminEmail, DateTimeOffset now, TimeSpan lease)
    {
        return new SignUpOperation
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = idempotencyKey is null ? null : fingerprint,
            AdminEmail = adminEmail,
            NormalizedEmail = Normalize(adminEmail),
            CompanyId = Guid.NewGuid(),
            AttemptCount = 1,
            LeaseToken = Guid.NewGuid(),
            LeaseExpiresAt = now + lease,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public static string Normalize(string email) => email.Trim().ToUpperInvariant();

    public void TakeLease(DateTimeOffset now, TimeSpan lease)
    {
        AttemptCount++;
        LeaseToken = Guid.NewGuid();
        LeaseExpiresAt = now + lease;
        UpdatedAt = now;
    }

    public void RenewLease(DateTimeOffset now, TimeSpan lease)
    {
        LeaseExpiresAt = now + lease;
        UpdatedAt = now;
    }

    public void ReleaseLease(string? lastError, DateTimeOffset now)
    {
        LeaseToken = null;
        LeaseExpiresAt = null;
        LastError = lastError;
        UpdatedAt = now;
    }

    public void BeginCompensation(string code, bool releaseKey, DateTimeOffset now, TimeSpan lease)
    {
        Stage = StageCompensating;
        CompensationCode = code;
        CompensationReleaseKey = releaseKey;
        LeaseExpiresAt = now + lease;
        UpdatedAt = now;
    }

    public void MarkSwept(DateTimeOffset now) => SweptAt = now;

    public void MarkCompanyProvisioned(DateTimeOffset now)
    {
        Stage = StageCompanyProvisioned;
        UpdatedAt = now;
    }

    public void MarkEmployeeCreated(Guid employeeId, DateTimeOffset now)
    {
        EmployeeId = employeeId;
        Stage = StageEmployeeCreated;
        UpdatedAt = now;
    }

    public void MarkIdentityCreated(Guid supabaseAuthUserId, DateTimeOffset now)
    {
        SupabaseAuthUserId = supabaseAuthUserId;
        Stage = StageIdentityCreated;
        UpdatedAt = now;
    }

    public void Complete(string responseJson, DateTimeOffset now)
    {
        ResponseJson = responseJson;
        Status = StatusCompleted;
        Stage = StageCompleted;
        LeaseToken = null;
        LeaseExpiresAt = null;
        LastError = null;
        CompletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records a compensated terminal failure. When <paramref name="releaseKey"/> is set the
    /// idempotency key is freed so a client may retry the same key with a fresh operation
    /// (used for infrastructure failures; deterministic conflicts keep the key bound so they replay).
    /// </summary>
    public void Fail(string code, string message, bool releaseKey, DateTimeOffset now)
    {
        Status = StatusFailed;
        Stage = StageCompensated;
        FailureCode = code;
        FailureMessage = message;
        LeaseToken = null;
        LeaseExpiresAt = null;
        CompletedAt = now;
        UpdatedAt = now;
        if (releaseKey)
        {
            IdempotencyKey = null;
        }
    }
}
