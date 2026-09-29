using HR.SharedKernel;

namespace HR.Modules.Identity.Domain;

internal sealed class UserInvite : IVersionedAggregate
{
    private readonly List<Guid> _pendingRoleIds = [];

    private UserInvite() { }

    public Guid Id { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid CompanyId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string Token { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public DateTimeOffset? EmailSentAt { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Ticket 2 (P1): guards against AcceptInvite and CancelInvite racing on the same invite — e.g.
    // an admin cancelling an invite at the same instant the invitee submits acceptance from a
    // stale page. Both handlers pin the Version they loaded before saving; whichever writes second
    // gets a concurrency conflict instead of silently overwriting the other's outcome.
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

    public IReadOnlyList<Guid> PendingRoleIds => _pendingRoleIds;

    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    public bool IsClaimed => ClaimedAt.HasValue;
    public bool IsCancelled => CancelledAt.HasValue;

    public static UserInvite Create(
        Guid employeeId,
        Guid companyId,
        string email,
        DateTimeOffset now,
        IEnumerable<Guid>? roleIds = null,
        Guid? createdByUserId = null)
    {
        var invite = new UserInvite
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            CompanyId = companyId,
            Email = email,
            Token = GenerateToken(),
            ExpiresAt = now.AddDays(7),
            CreatedAt = now,
            CreatedByUserId = createdByUserId,
        };

        if (roleIds is not null)
            invite._pendingRoleIds.AddRange(roleIds.Distinct());

        return invite;
    }

    public void Claim(DateTimeOffset now)
    {
        ClaimedAt = now;
    }

    public void MarkEmailSent(DateTimeOffset now)
    {
        EmailSentAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        CancelledAt = now;
    }

    public void Resend(DateTimeOffset now)
    {
        Token = GenerateToken();
        ExpiresAt = now.AddDays(7);
    }

    private static string GenerateToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
