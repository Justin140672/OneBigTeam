namespace HR.Modules.Identity.Domain;

internal sealed class UserInvite
{
    private UserInvite() { }

    public Guid Id { get; private set; }

    /// <summary>The employee this invite is for. On accept, the ApplicationUser.Id will equal this value.</summary>
    public Guid EmployeeId { get; private set; }

    public Guid CompanyId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string Token { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    public bool IsClaimed => ClaimedAt.HasValue;

    public static UserInvite Create(
        Guid employeeId,
        Guid companyId,
        string email,
        DateTimeOffset now)
    {
        return new UserInvite
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            CompanyId = companyId,
            Email = email,
            Token = GenerateToken(),
            ExpiresAt = now.AddDays(7),
            CreatedAt = now,
        };
    }

    public void Claim(DateTimeOffset now)
    {
        ClaimedAt = now;
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
