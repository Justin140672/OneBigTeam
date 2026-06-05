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
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ApplicationUser Create(
        Guid id,
        string email,
        string passwordHash,
        string firstName,
        string lastName,
        DateTimeOffset now)
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
            CreatedAt = now,
            UpdatedAt = now,
        };
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
}
