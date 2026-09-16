namespace HR.Modules.Identity.Domain;

/// <summary>
/// Ticket 8 (P2): a durable record of an in-progress invitation acceptance, persisted BEFORE
/// AcceptInvite calls Supabase to create the confirmed Auth user. AcceptInvite previously called
/// Supabase, then committed the local UserProfile/UserRoles/invite-claim afterward with no recovery
/// path between those two steps — if the process died (or the DB save failed) after Supabase
/// succeeded but before the local commit, a retry hit ISupabaseAuthGateway.CreateConfirmedUserAsync
/// again, got EmailAlreadyRegisteredException, and the invitee was permanently stuck with a
/// "an account with this email already exists" conflict for an account only THEY (via this exact
/// invite) actually created.
///
/// This record lets AcceptInvite safely resume: when a retry finds an existing Pending operation
/// for this invite AND Supabase reports the email already registered, it resolves the real Supabase
/// user id (via ISupabaseAuthGateway.GetUserIdByEmailAsync) and confirms no OTHER UserProfile is
/// already linked to that id (guarding against ever attaching to a genuinely unrelated pre-existing
/// account) before resuming local provisioning with it.
/// </summary>
internal sealed class InviteAcceptanceOperation
{
    private InviteAcceptanceOperation() { }

    public const string StatusPending           = "pending";
    public const string StatusSupabaseConfirmed = "supabase_confirmed";
    public const string StatusCompleted         = "completed";

    public Guid Id { get; private set; }
    public Guid InviteId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public Guid? SupabaseAuthUserId { get; private set; }
    public string Status { get; private set; } = StatusPending;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static InviteAcceptanceOperation CreatePending(
        Guid id, Guid inviteId, Guid companyId, Guid employeeId, string email, DateTimeOffset now)
    {
        return new InviteAcceptanceOperation
        {
            Id = id,
            InviteId = inviteId,
            CompanyId = companyId,
            EmployeeId = employeeId,
            Email = email,
            Status = StatusPending,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void MarkSupabaseConfirmed(Guid supabaseAuthUserId, DateTimeOffset now)
    {
        SupabaseAuthUserId = supabaseAuthUserId;
        Status = StatusSupabaseConfirmed;
        UpdatedAt = now;
    }

    public void MarkCompleted(DateTimeOffset now)
    {
        Status = StatusCompleted;
        UpdatedAt = now;
        CompletedAt = now;
    }
}
