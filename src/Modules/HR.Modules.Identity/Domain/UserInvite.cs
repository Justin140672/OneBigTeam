namespace HR.Modules.Identity.Domain;

internal sealed class UserInvite
{
    private readonly List<Guid> _pendingRoleIds = [];

    private UserInvite() { }

    public Guid Id { get; private set; }

    /// <summary>The employee this invite is for. On accept, the ApplicationUser.Id will equal this value.</summary>
    public Guid EmployeeId { get; private set; }

    public Guid CompanyId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string Token { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Roles to assign once this invite is accepted. Empty means "fall back to the base Employee role".</summary>
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

    public void Cancel(DateTimeOffset now)
    {
        CancelledAt = now;
    }

    /// <summary>Regenerates the token and expiry so the invite can be shared again.</summary>
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
