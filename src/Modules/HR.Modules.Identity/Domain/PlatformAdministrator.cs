namespace HR.Modules.Identity.Domain;

// A platform-level administrator account — not tied to any company. This is the real
// authorization source for the Admin Portal's "administrator management" screen, replacing the
// static PlatformAdmin:AllowedEmails config allow-list for that screen only. Existing handlers
// elsewhere that still check the config allow-list are unaffected by this entity.
internal sealed class PlatformAdministrator
{
    private PlatformAdministrator() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;

    /// <summary>May be null if a Supabase Auth user has not yet been provisioned/linked for this administrator.</summary>
    public Guid? SupabaseAuthUserId { get; private set; }

    public PlatformAdministratorRole Role { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Null means system-seeded (e.g. bootstrap seeding from configuration).</summary>
    public Guid? CreatedByUserId { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }
    public Guid? DisabledByUserId { get; private set; }

    public static PlatformAdministrator Create(
        string email,
        PlatformAdministratorRole role,
        DateTimeOffset now,
        Guid? createdByUserId = null,
        Guid? supabaseAuthUserId = null)
    {
        return new PlatformAdministrator
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            SupabaseAuthUserId = supabaseAuthUserId,
            Role = role,
            IsEnabled = true,
            CreatedAt = now,
            CreatedByUserId = createdByUserId,
        };
    }

    public void Disable(DateTimeOffset now, Guid? actorUserId)
    {
        IsEnabled = false;
        DisabledAt = now;
        DisabledByUserId = actorUserId;
    }

    public void Enable(DateTimeOffset now)
    {
        IsEnabled = true;
        DisabledAt = null;
        DisabledByUserId = null;
    }

    public void AssignRole(PlatformAdministratorRole newRole)
    {
        Role = newRole;
    }
}
