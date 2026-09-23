using Hangfire;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services.AccountEmailPolicy;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.QueueInvitationBatch;

internal sealed class QueueInvitationBatchHandler(
    IdentityDbContext db,
    IClock clock,
    IEmployeeInviteCandidateReader inviteCandidateReader,
    IBackgroundJobClient backgroundJobClient,
    IAuditEventPublisher auditEventPublisher,
    AccountCreationEmailGuard accountCreationEmailGuard)
{
    public async Task<Result<QueueInvitationBatchResponse>> HandleAsync(
        QueueInvitationBatchRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(nameof(QueueInvitationBatchHandler), request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, QueueInvitationBatchResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<QueueInvitationBatchResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        // NOTE: the DB-level unique (company_id, idempotency_key) partial index (see
        // InvitationBatchConfiguration) is what actually prevents a duplicate batch when a caller
        // supplies a key; the TryReplayAsync check above additionally lets that caller receive back
        // the SAME response on retry. A caller that never supplies a key has no such protection here
        // — accepted as a caller risk, not solved by additional application-level locking.

        var candidates = await inviteCandidateReader.GetCandidatesAsync(request.CompanyId, cancellationToken);
        var candidatesById = candidates.ToDictionary(c => c.EmployeeId);

        var requestedIds = request.EmployeeIds.Distinct().ToList();

        var accountIds = await db.Users
            .AsNoTracking()
            .Where(u => requestedIds.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var profileIds = await db.UserProfiles
            .AsNoTracking()
            .Where(p => requestedIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var openInvites = await db.UserInvites
            .AsNoTracking()
            .Where(i => i.CompanyId == request.CompanyId
                && requestedIds.Contains(i.EmployeeId)
                && i.ClaimedAt == null
                && i.CancelledAt == null)
            .ToListAsync(cancellationToken);

        var pendingInviteEmployeeIds = openInvites
            .Where(i => !i.IsExpired)
            .Select(i => i.EmployeeId)
            .ToHashSet();

        var hasAccountIds = accountIds.Concat(profileIds).ToHashSet();

        var excluded = new List<ExcludedInvitationCandidate>();
        var resolved = new List<(Guid EmployeeId, string Email)>();
        var rejectedByEmailPolicy = new List<(Guid? SubjectEmployeeId, AccountEmailDomainEvaluation Evaluation)>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateEmails = requestedIds
            .Select(id => candidatesById.TryGetValue(id, out var c) ? c.WorkEmail : null)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .GroupBy(e => e!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var employeeId in requestedIds)
        {
            if (!candidatesById.TryGetValue(employeeId, out var candidate))
            {
                excluded.Add(new ExcludedInvitationCandidate(employeeId, null, "NotEligible"));
                continue;
            }

            if (hasAccountIds.Contains(employeeId))
            {
                excluded.Add(new ExcludedInvitationCandidate(employeeId, candidate.WorkEmail, "AlreadyHasAccount"));
                continue;
            }

            if (pendingInviteEmployeeIds.Contains(employeeId))
            {
                excluded.Add(new ExcludedInvitationCandidate(employeeId, candidate.WorkEmail, "AlreadyInvited"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate.WorkEmail))
            {
                excluded.Add(new ExcludedInvitationCandidate(employeeId, null, "MissingEmail"));
                continue;
            }

            // Ticket 9: a public/disposable (or unparseable) work email can't be used to create an
            // account — excluded individually, never queued, so no invite row or email is ever
            // created for it while the rest of the batch still proceeds.
            var emailEvaluation = accountCreationEmailGuard.Evaluate(candidate.WorkEmail);
            if (!emailEvaluation.IsAllowed)
            {
                excluded.Add(new ExcludedInvitationCandidate(
                    employeeId, candidate.WorkEmail, AccountCreationEmailGuard.BulkExclusionReason));
                rejectedByEmailPolicy.Add((employeeId, emailEvaluation));
                continue;
            }

            if (duplicateEmails.Contains(candidate.WorkEmail))
            {
                excluded.Add(new ExcludedInvitationCandidate(employeeId, candidate.WorkEmail, "DuplicateEmail"));
                continue;
            }

            if (!seenEmails.Add(candidate.WorkEmail))
            {
                // Defensive: shouldn't happen given the duplicateEmails check above, but guards
                // against creating two recipients with the same email regardless.
                excluded.Add(new ExcludedInvitationCandidate(employeeId, candidate.WorkEmail, "DuplicateEmail"));
                continue;
            }

            resolved.Add((employeeId, candidate.WorkEmail));
        }

        // Ticket 9: one audit row (domains + employee ids only, no addresses) for every recipient
        // rejected by the email-domain policy in this request.
        await accountCreationEmailGuard.RecordRejectionsAsync(
            AccountCreationPath.BulkEmployeeInvitation, request.CompanyId, rejectedByEmailPolicy, actorUserId, cancellationToken);

        if (resolved.Count == 0)
        {
            if (rejectedByEmailPolicy.Count > 0)
            {
                // Name every affected address so the administrator knows exactly which employee
                // records need an organisation email before they can be invited.
                var rejectedEmails = excluded
                    .Where(e => e.Reason == AccountCreationEmailGuard.BulkExclusionReason)
                    .Select(e => e.Email)
                    .ToList();

                return Result.Failure<QueueInvitationBatchResponse>(new Error(
                    AccountCreationEmailGuard.WorkEmailRequiredCode,
                    "No eligible recipients remain after resolving the submitted employees. " +
                    $"{AccountCreationEmailGuard.BulkExclusionMessage} These employees have a public or personal " +
                    $"email address as their work email: {string.Join(", ", rejectedEmails)}."));
            }

            return Result.Failure<QueueInvitationBatchResponse>(
                Error.Validation("No eligible recipients remain after resolving the submitted employees."));
        }

        var now = clock.UtcNow;
        var batch = InvitationBatch.Create(request.CompanyId, actorUserId ?? Guid.Empty, now, request.IdempotencyKey);
        db.InvitationBatches.Add(batch);

        foreach (var (employeeId, email) in resolved)
        {
            db.InvitationBatchRecipients.Add(InvitationBatchRecipient.Create(batch.Id, employeeId, email, now));
        }

        var response = new QueueInvitationBatchResponse(batch.Id, resolved.Count, excluded);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, QueueInvitationBatchResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        // Enqueue only after the save has committed, so a crash before this point leaves a Queued
        // batch that never got processed rather than a job racing an uncommitted transaction.
        backgroundJobClient.Enqueue<ProcessInvitationBatchJob>(job => job.RunAsync(batch.Id, CancellationToken.None));

        await auditEventPublisher.PublishAsync(
            new InvitationBatchQueuedAuditEvent(
                request.CompanyId,
                batch.Id,
                resolved.Select(r => r.EmployeeId).ToList(),
                actorUserId,
                now),
            cancellationToken);

        return Result.Success(response);
    }
}
