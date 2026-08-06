namespace HR.Modules.Identity.Domain;

internal sealed class UserProfile
{
    private UserProfile() { }

    public Guid Id { get; private set; }
    public Guid SupabaseAuthUserId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static UserProfile Create(
        Guid id,
        Guid supabaseAuthUserId,
        Guid companyId,
        string email,
        string firstName,
        string lastName,
        DateTimeOffset now)
    {
        return new UserProfile
        {
            Id = id,
            SupabaseAuthUserId = supabaseAuthUserId,
            CompanyId = companyId,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void UpdateName(string firstName, string lastName, DateTimeOffset now)
    {
        FirstName = firstName;
        LastName = lastName;
        UpdatedAt = now;
    }

    public void UpdateEmail(string email, DateTimeOffset now)
    {
        Email = email;
        UpdatedAt = now;
    }

    /// <summary>
    /// Development-only self-heal: corrects a UserProfile row seeded with a stale/wrong
    /// SupabaseAuthUserId (e.g. from an earlier buggy seeding run) so it matches the "sub" claim
    /// actually issued on dev-persona tokens going forward. See IdentityModule.SeedDevSupabaseUsersAsync.
    /// </summary>
    public void UpdateSupabaseAuthUserId(Guid supabaseAuthUserId, DateTimeOffset now)
    {
        SupabaseAuthUserId = supabaseAuthUserId;
        UpdatedAt = now;
    }
}
