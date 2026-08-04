namespace HR.Modules.Identity.Domain;

internal sealed class ApplicationUser
{
    private ApplicationUser() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    // Distinct from IsActive (enabled/disabled by an admin): this tracks whether the account has
    // completed the email-confirmation step. Every existing path (dev seed, AcceptInvite) creates
    // already-confirmed accounts — only self-service SignUp creates one pending confirmation,
    // since that's the only flow with no administrator vouching for the email address. Real
    // Supabase Auth (and an actual confirmation email) is out of scope for now — see
    // Features/ConfirmEmail for the interim stub.
    public bool IsEmailConfirmed { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ApplicationUser Create(
        Guid id,
        string email,
        string passwordHash,
        string firstName,
        string lastName,
        DateTimeOffset now,
        bool isEmailConfirmed = true)
    {
        return new ApplicationUser
        {
            Id = id,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = passwordHash,
            FirstName = firstName,
            LastName = lastName,
            IsActive = true,
            IsEmailConfirmed = isEmailConfirmed,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void ConfirmEmail(DateTimeOffset now)
    {
        IsEmailConfirmed = true;
        UpdatedAt = now;
    }

    public void UpdateProfile(string firstName, string lastName, DateTimeOffset now)
    {
        FirstName = firstName;
        LastName = lastName;
        UpdatedAt = now;
    }

    public void ChangeEmail(string email, DateTimeOffset now)
    {
        Email = email;
        NormalizedEmail = email.ToUpperInvariant();
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }

    public void RecordLogin(DateTimeOffset now)
    {
        LastLoginAt = now;
    }
}
