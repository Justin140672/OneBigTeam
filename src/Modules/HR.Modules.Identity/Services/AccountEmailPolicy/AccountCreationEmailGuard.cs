using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Services.AccountEmailPolicy;

/// <summary>Ticket 9: every production path that can create a new One Big Team user account.</summary>
internal enum AccountCreationPath
{
    PublicSignup,
    EmployeeInvitation,
    BulkEmployeeInvitation,
    InvitationAcceptance,
    PlatformAdministrator,
}

/// <summary>
/// Ticket 9: authoritative, server-side enforcement point for the account-creation email-domain
/// policy. Every account-creation handler calls this BEFORE persisting anything, calling Supabase or
/// sending any email, so the rule holds even when a handler is invoked directly (bypassing the
/// FastEndpoints validator pipeline). On rejection it logs non-personal diagnostic information (path,
/// count) and records an <see cref="AccountCreationEmailRejectedAuditEvent"/> — the restricted audit
/// store is where business-required domain data belongs.
///
/// The rejection response never reveals whether an account already exists for the address — the
/// policy is evaluated before any account lookup in every path.
/// </summary>
internal sealed class AccountCreationEmailGuard(
    IAccountEmailDomainPolicy policy,
    IAuditEventPublisher auditEventPublisher,
    IClock clock,
    ILogger<AccountCreationEmailGuard> logger)
{
    public const string WorkEmailRequiredCode = "work_email_required";

    public const string WorkEmailRequiredMessage =
        "Please use your organisation's work email address. Public email services such as Gmail, Hotmail and Outlook.com cannot be used to create an account.";

    /// <summary>Bulk-invitation exclusion reason code (see QueueInvitationBatch / ProcessInvitationBatchJob).</summary>
    public const string BulkExclusionReason = "PublicEmailDomain";

    public const string BulkExclusionMessage = "An organisation email address is required to create an account.";

    public static Error WorkEmailRequired { get; } = new(WorkEmailRequiredCode, WorkEmailRequiredMessage);

    public static Error InvalidEmail { get; } = Error.Validation("Enter a valid email address.");

    public AccountEmailDomainEvaluation Evaluate(string? email) => policy.Evaluate(email);

    public static Error ErrorFor(AccountEmailDomainEvaluation evaluation) =>
        evaluation.Verdict == AccountEmailDomainVerdict.Malformed ? InvalidEmail : WorkEmailRequired;

    /// <summary>
    /// Evaluates a single address; on rejection records the audit event and returns the failure to
    /// hand straight back to the caller.
    /// </summary>
    public async Task<Result> EnsureAllowedAsync(
        string? email,
        AccountCreationPath path,
        Guid companyId,
        Guid? subjectEmployeeId,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var evaluation = policy.Evaluate(email);
        if (evaluation.IsAllowed)
            return Result.Success();

        await RecordRejectionsAsync(
            path, companyId, [(subjectEmployeeId, evaluation)], actorUserId, cancellationToken);

        return Result.Failure(ErrorFor(evaluation));
    }

    /// <summary>
    /// Records one audit event (and one privacy-safe warning log) for a set of rejected addresses —
    /// used directly by the bulk-invitation paths so a large batch produces a single audit row.
    /// </summary>
    public async Task RecordRejectionsAsync(
        AccountCreationPath path,
        Guid companyId,
        IReadOnlyCollection<(Guid? SubjectEmployeeId, AccountEmailDomainEvaluation Evaluation)> rejections,
        Guid? actorUserId,
        CancellationToken cancellationToken,
        AuditActorType? actorType = null)
    {
        if (rejections.Count == 0)
            return;

        var domains = rejections
            .Select(r => r.Evaluation.Domain ?? "(malformed)")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

        var subjectEmployeeIds = rejections
            .Where(r => r.SubjectEmployeeId.HasValue)
            .Select(r => r.SubjectEmployeeId!.Value)
            .Distinct()
            .ToList();

        logger.LogWarning(
            "Account creation rejected on {AccountCreationPath}: {RejectedCount} address(es)",
            path, rejections.Count);

        var resolvedActorType = actorType
            ?? (actorUserId.HasValue ? AuditActorType.Human : AuditActorType.Anonymous);

        await auditEventPublisher.PublishAsync(
            new AccountCreationEmailRejectedAuditEvent(
                companyId,
                EntityId: subjectEmployeeIds.Count == 1 ? subjectEmployeeIds[0] : Guid.NewGuid(),
                Path: PathName(path),
                Domains: domains,
                SubjectEmployeeIds: subjectEmployeeIds,
                RejectedCount: rejections.Count,
                ActorUserId: actorUserId,
                ActorKind: resolvedActorType,
                OccurredAt: clock.UtcNowOffset()),
            cancellationToken);
    }

    internal static string PathName(AccountCreationPath path) => path switch
    {
        AccountCreationPath.PublicSignup => "public-signup",
        AccountCreationPath.EmployeeInvitation => "employee-invitation",
        AccountCreationPath.BulkEmployeeInvitation => "bulk-employee-invitation",
        AccountCreationPath.InvitationAcceptance => "invitation-acceptance",
        AccountCreationPath.PlatformAdministrator => "platform-administrator",
        _ => path.ToString(),
    };
}

/// <summary>
/// Ticket 9: thrown by <see cref="SupabaseAuthGateway"/> when asked to create an identity-provider
/// account for a non-permitted email domain. Handlers check the policy first, so reaching this
/// means a code path skipped <see cref="AccountCreationEmailGuard"/> — it is a programming error,
/// deliberately surfaced rather than silently creating the account.
/// </summary>
internal sealed class AccountEmailDomainNotPermittedException(string? domain)
    : InvalidOperationException(
        $"Ticket 9: refusing to create an authentication account for email domain '{domain ?? "(malformed)"}'.");
