using FastEndpoints;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HR.Modules.Identity.Features.AcceptInvite;

// Creates a real Supabase-backed UserProfile (not a local-auth ApplicationUser — that Phase A
// stand-in has been superseded here the same way SignUp's was, see SignUpHandler's remarks) so an
// invited employee can actually use every Supabase-based flow: real password-grant Login,
// RequestPasswordReset/forgot-password (which only ever looks at UserProfiles — an
// ApplicationUser-only account was silently invisible to it), etc. Uses
// ISupabaseAuthGateway.CreateConfirmedUserAsync rather than CreateUserAsync: the invite link
// itself, emailed to invite.Email when the invite was sent, already proves ownership of that
// address, so there's no separate "verify your email" step needed here the way self-service
// SignUp needs one.
internal sealed class Endpoint(
    IdentityDbContext db,
    ISupabaseAuthGateway supabaseAuthGateway,
    IClock clock) : Endpoint<AcceptInviteRequest, AcceptInviteResponse>
{
    public override void Configure()
    {
        Post("/api/invites/accept");
        AllowAnonymous();
    }

    public override async Task HandleAsync(AcceptInviteRequest req, CancellationToken ct)
    {
        var invite = await db.UserInvites
            .FirstOrDefaultAsync(i => i.Token == req.Token, ct);

        if (invite is null)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = "Invite not found." }));
            return;
        }

        if (invite.IsClaimed)
        {
            await Send.ResultAsync(TypedResults.Conflict(new { error = "This invite has already been used." }));
            return;
        }

        // Ticket 2 (P1): CancelInvite only sets CancelledAt — the token and row are otherwise left
        // intact — so without this check a cancelled invite (including one carrying privileged
        // roles) stayed fully usable until its natural expiry.
        if (invite.IsCancelled)
        {
            await Send.ResultAsync(TypedResults.Conflict(new { error = "This invite has been cancelled." }));
            return;
        }

        var now = clock.UtcNow;

        if (invite.IsExpired)
        {
            await Send.ResultAsync(TypedResults.BadRequest(new { error = "This invite has expired." }));
            return;
        }

        // Use the employee ID as the user ID — single identity across modules (UserRole.UserId
        // below relies on this matching, same as SignUpHandler's admin profile).
        InviteAcceptanceOperation? operation = null;
        Guid? newProfileSupabaseUserId = null;

        var profileExists = await db.UserProfiles.AnyAsync(p => p.Id == invite.EmployeeId, ct);
        if (!profileExists)
        {
            // Ticket 8 (P2): persist a durable operation record BEFORE calling Supabase, so a retry
            // after a crash/failure between "Supabase user created" and "local commit" can tell that
            // apart from a genuinely fresh first attempt (see InviteAcceptanceOperation remarks).
            operation = await db.InviteAcceptanceOperations
                .FirstOrDefaultAsync(o => o.InviteId == invite.Id, ct);

            if (operation is null)
            {
                var candidate = InviteAcceptanceOperation.CreatePending(
                    Guid.NewGuid(), invite.Id, invite.CompanyId, invite.EmployeeId, invite.Email, now);
                db.InviteAcceptanceOperations.Add(candidate);

                try
                {
                    await db.SaveChangesAsync(ct);
                    operation = candidate;
                }
                catch (DbUpdateException ex) when (PostgresUniqueViolation.Is(ex))
                {
                    // Ticket 24 (P1) req #2/#4: another first-time acceptance request beat us to
                    // creating the operation for this exact invite between our null-check above and
                    // our insert here (InviteAcceptanceOperation.InviteId has a unique index). Detach
                    // our failed candidate row so this (shared, scoped) DbContext stays safe to
                    // reuse, then load and validate the winning operation. We must NOT proceed to
                    // call Supabase ourselves from here: doing so would mean two requests
                    // independently calling CreateConfirmedUserAsync — potentially with two
                    // different passwords — under circumstances that could converge on the same
                    // provisioning correlation id. The loser's password must never silently replace
                    // or override the winner's.
                    db.Entry(candidate).State = EntityState.Detached;

                    await SendLostOperationRaceResultAsync(invite, ct);
                    return;
                }
            }

            Guid supabaseUserId;
            try
            {
                // Ticket 12 (P1): stamps this operation's own id onto the new Supabase user's
                // metadata at creation time — the ONLY thing that later proves "this exact operation
                // (not some unrelated earlier or foreign account) created this Supabase user" when a
                // retry hits EmailAlreadyRegisteredException below. A pending local operation row
                // alone is never sufficient proof: a genuinely first attempt also creates one before
                // ever calling Supabase, so its mere existence can't distinguish "we made this" from
                // "someone/something else already had this email".
                supabaseUserId = await supabaseAuthGateway.CreateConfirmedUserAsync(
                    invite.Email, req.Password, ct,
                    metadata: new Dictionary<string, string>
                    {
                        ["provisioning_operation_id"] = operation.Id.ToString(),
                    });
            }
            catch (EmailAlreadyRegisteredException)
            {
                // Ticket 12 (P1): "already registered" now only ever resumes provisioning when the
                // existing Supabase user's own metadata carries THIS operation's id — proving THIS
                // invite's own earlier (interrupted) attempt created it — never merely because a
                // local Pending operation happens to exist (see class remarks above; that alone is
                // indistinguishable from a normal first attempt) and never merely because no other
                // UserProfile is linked to it (a genuinely pre-existing or foreign identity would
                // also pass that check). Any other case — no matching account, a match with no/wrong
                // correlation value — is rejected as a conflict rather than silently linking the
                // invite to an account whose ownership was never actually proven, and the invitee's
                // supplied password is never silently discarded in favour of an existing unrelated
                // account.
                var resolved = await supabaseAuthGateway.GetUserMetadataByEmailAsync(invite.Email, ct);

                var provesThisOperationCreatedIt =
                    resolved is not null
                    && resolved.Value.Metadata.TryGetValue("provisioning_operation_id", out var correlationId)
                    && correlationId == operation.Id.ToString();

                if (!provesThisOperationCreatedIt)
                {
                    await Send.ResultAsync(TypedResults.Conflict(
                        new { error = "An account with this email already exists." }));
                    return;
                }

                var linkedToAnotherProfile = await db.UserProfiles.AnyAsync(
                    p => p.SupabaseAuthUserId == resolved!.Value.UserId, ct);

                if (linkedToAnotherProfile)
                {
                    await Send.ResultAsync(TypedResults.Conflict(
                        new { error = "An account with this email already exists." }));
                    return;
                }

                supabaseUserId = resolved!.Value.UserId;
            }

            operation.MarkSupabaseConfirmed(supabaseUserId, now);
            newProfileSupabaseUserId = supabaseUserId;

            // Ticket 8 (P2): persist the SupabaseConfirmed transition on its own BEFORE attempting
            // the atomic local commit below. If the process dies inside the atomic transaction
            // (which never partially commits — see CommitLocalAcceptanceAsync), a retry/reconciler
            // must still be able to see that Supabase provisioning itself already succeeded for this
            // operation, independent of whether the local commit ever lands.
            await db.SaveChangesAsync(ct);
        }

        // Assign the roles selected when the invite was sent (Features/InviteEmployeeUser),
        // falling back to the base Employee role for invites created before role selection
        // existed (e.g. via the older SendInvite endpoint).
        var roleIds = invite.PendingRoleIds.Count > 0
            ? invite.PendingRoleIds
            : [SystemRoles.Employee];

        // Ticket 26 (P1): everything below commits as ONE explicit PostgreSQL transaction —
        // UserProfile creation, every UserRole assignment, the invite claim, and the operation's
        // completion either all land together or none of them do. This replaces the previous
        // approach of saving the profile, then each role, then the invite claim as separate
        // committed transactions, which could leave a cancelled invite having already granted a
        // committed UserProfile and/or privileged UserRole rows before the final claim discovered
        // the cancellation.
        var commitResult = await CommitLocalAcceptanceAsync(
            invite, operation, newProfileSupabaseUserId, roleIds, now, ct);

        if (!commitResult.IsSuccess)
        {
            if (commitResult.Error.Code == "concurrency")
            {
                // Someone else (most likely CancelInvite, or another AcceptInvite request) modified
                // this invite between our read and our write — re-check its current state so the
                // response is accurate rather than a generic conflict. Because the whole local commit
                // above ran in a single transaction that has now rolled back in its entirety, no
                // profile or role rows from this attempt remain committed.
                var current = await db.UserInvites.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invite.Id, ct);
                if (current?.IsCancelled == true)
                {
                    await Send.ResultAsync(TypedResults.Conflict(new { error = "This invite has been cancelled." }));
                    return;
                }

                if (current?.IsClaimed == true)
                {
                    await Send.ResultAsync(TypedResults.Conflict(new { error = "This invite has already been used." }));
                    return;
                }

                await Send.ResultAsync(TypedResults.Conflict(new { error = "This invite could not be accepted. Please try again." }));
                return;
            }

            // Ticket 26 (P1) req #3: a duplicate-acceptance race — another concurrent request for
            // this same invite already committed its own atomic transaction (profile + roles + claim
            // + operation completion) before ours reached the same unique constraint(s). Our entire
            // attempt rolled back, so re-check the invite: if it is now claimed, the winner
            // succeeded and this caller gets a controlled conflict rather than a 500.
            var winner = await db.UserInvites.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invite.Id, ct);
            if (winner?.IsClaimed == true)
            {
                await Send.ResultAsync(TypedResults.Conflict(new { error = "This invite has already been used." }));
                return;
            }

            await Send.ResultAsync(TypedResults.Conflict(new { error = "This invite could not be accepted. Please try again." }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(new AcceptInviteResponse(invite.EmployeeId)));
    }

    /// <summary>
    /// Ticket 26 (P1): commits UserProfile creation (when <paramref name="newProfileSupabaseUserId"/>
    /// is set), every UserRole assignment, the invite claim, and the operation's completion in a
    /// single explicit PostgreSQL transaction backed by one <see cref="DbContext.SaveChangesAsync"/>
    /// call. The invite's optimistic-concurrency version is pinned so a concurrent winner (another
    /// AcceptInvite request, or CancelInvite) causes the whole batch — profile insert, role inserts,
    /// invite update, operation update alike — to fail together and roll back together; nothing here
    /// is ever committed piecemeal.
    /// </summary>
    private async Task<Result> CommitLocalAcceptanceAsync(
        UserInvite invite,
        InviteAcceptanceOperation? operation,
        Guid? newProfileSupabaseUserId,
        IReadOnlyCollection<Guid> roleIds,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        try
        {
            if (newProfileSupabaseUserId is { } supabaseUserId)
            {
                var profile = UserProfile.Create(
                    invite.EmployeeId, supabaseUserId, invite.CompanyId, invite.Email,
                    firstName: string.Empty, lastName: string.Empty, now);
                db.UserProfiles.Add(profile);
            }

            foreach (var roleId in roleIds)
            {
                var roleExists = await db.UserRoles.AsNoTracking().AnyAsync(
                    ur => ur.UserId == invite.EmployeeId && ur.RoleId == roleId, ct);
                if (roleExists)
                    continue;

                db.UserRoles.Add(UserRole.Create(invite.EmployeeId, roleId, now));
            }

            var expectedVersion = invite.Version;
            invite.Claim(now);
            operation?.MarkCompleted(now);

            db.Entry(invite).Property(nameof(IVersionedAggregate.Version)).OriginalValue = expectedVersion;
            invite.IncrementVersion();

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Ticket 26 (P1) req #2/#4: CancelInvite (or another AcceptInvite request) won the race
            // on the invite's version — roll back the whole transaction so the profile insert and
            // every role insert attempted above are undone together with the failed invite update.
            await SafeRollbackAsync(transaction, ct);
            return Result.Failure(Error.Concurrency("This invite is no longer valid."));
        }
        catch (DbUpdateException ex) when (PostgresUniqueViolation.Is(ex))
        {
            // Ticket 26 (P1) req #3: a concurrent acceptance attempt for this same invite already
            // committed (most likely the identical UserProfile row, whose primary key is the
            // employee id, or one of the same UserRole rows). Roll back this attempt in full rather
            // than resuming with a mix of detached/committed state — the caller is told to treat this
            // as a conflict/replay (see the caller's post-commit handling), and the winner's own
            // transaction already completed the full profile+roles+claim+operation set atomically.
            await SafeRollbackAsync(transaction, ct);
            return Result.Failure(Error.Conflict("This invite could not be accepted. Please try again."));
        }
    }

    private static async Task SafeRollbackAsync(IDbContextTransaction transaction, CancellationToken ct)
    {
        try
        {
            await transaction.RollbackAsync(ct);
        }
        catch
        {
            // The transaction may already be aborted server-side by the failure that triggered this
            // rollback attempt; disposal (via the caller's `await using`) still guarantees cleanup.
        }
    }

    /// <summary>
    /// Ticket 24 (P1) req #6: called when this request lost the race to create the
    /// <see cref="InviteAcceptanceOperation"/> for this invite (see the unique-constraint catch
    /// above). Reloads the invite so the response reflects the winner's outcome rather than the
    /// stale state this request read at the top of <see cref="HandleAsync"/>, and never attempts any
    /// local provisioning or Supabase call itself.
    /// </summary>
    private async Task SendLostOperationRaceResultAsync(UserInvite invite, CancellationToken ct)
    {
        var current = await db.UserInvites.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invite.Id, ct);

        if (current?.IsCancelled == true)
        {
            await Send.ResultAsync(TypedResults.Conflict(new { error = "This invite has been cancelled." }));
            return;
        }

        if (current?.IsClaimed == true)
        {
            await Send.ResultAsync(TypedResults.Conflict(new { error = "This invite has already been used." }));
            return;
        }

        // The winning request is still provisioning (or has just finished and the invite hasn't yet
        // reflected as claimed above, due to an ordinary read-after-write gap). Ask the client to
        // retry shortly rather than risk this request touching Supabase or local identity rows.
        await Send.ResultAsync(TypedResults.Conflict(new
        {
            error = "This invitation is already being accepted by another request. Please try again shortly.",
        }));
    }
}

internal sealed record AcceptInviteRequest(string Token, string Password);

internal sealed record AcceptInviteResponse(Guid UserId);
