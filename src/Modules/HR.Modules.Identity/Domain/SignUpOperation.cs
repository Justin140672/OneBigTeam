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
    public const string StageCompensated = "compensated";

    public const string ProvisioningMetadataKey = "provisioning_operation_id";

    public Guid Id { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
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
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

    public bool IsInProgress => Status == StatusInProgress;

    public bool LeaseIsActive(DateTimeOffset now) => LeaseExpiresAt is { } expires && expires > now;

    public static SignUpOperation Claim(
        string? idempotencyKey, string fingerprint, string adminEmail, DateTimeOffset now, TimeSpan lease)
    {
        return new SignUpOperation
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint,
            AdminEmail = adminEmail,
            NormalizedEmail = Normalize(adminEmail),
            CompanyId = Guid.NewGuid(),
            AttemptCount = 1,
            LeaseExpiresAt = now + lease,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public static string Normalize(string email) => email.Trim().ToUpperInvariant();

    public void TakeLease(DateTimeOffset now, TimeSpan lease)
    {
        AttemptCount++;
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
        LeaseExpiresAt = null;
        LastError = lastError;
        UpdatedAt = now;
    }

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
        LeaseExpiresAt = null;
        CompletedAt = now;
        UpdatedAt = now;
        if (releaseKey)
        {
            IdempotencyKey = null;
        }
    }
}
