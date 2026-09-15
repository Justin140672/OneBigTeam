using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Services;

/// <summary>
/// Ticket 6 follow-up: authoritative, converge-style reconciliation of identity.user_positions
/// against each employee's CURRENT position in HR.Modules.Employees (via
/// <see cref="IEmployeeAudienceReader"/>). Unlike the original IAM-03 backfill this superseded
/// (which was additive-only — it added a missing assignment but never expired a stale one), this
/// service actively converges every employee's assignment set to match their current authoritative
/// position on every run:
///
/// - Expires every assignment that is no longer the employee's current position (recovers a failed
///   revocation, e.g. a crashed/duplicated/out-of-order EmployeePositionChangedIntegrationEvent that
///   left a previous position's grant active).
/// - Reopens an existing-but-expired assignment for the employee's current position instead of
///   inserting a duplicate composite-key (UserId, PositionId) row.
/// - Creates a fresh assignment when none exists for the current position.
/// - Expires every assignment when the employee currently has no position at all.
/// - Still expires stale assignments for an employee whose CURRENT position cannot be resolved
///   right now (e.g. a transient read failure or a genuinely deleted PositionProfile) — an
///   unresolved new position never justifies retaining grants for a position the employee no
///   longer holds. It never guesses or creates an assignment against an unresolved position; that
///   part is retried on a later pass once resolution succeeds.
/// - Never touches identity.user_roles or identity.employee_role_overrides — direct roles and
///   overrides are a completely separate grant source (see IdentityAuthorizationService) and always
///   survive this reconciliation untouched.
///
/// Failures are isolated per employee: one employee's exception (including losing a concurrency
/// race against a live OnEmployeePositionChanged event for the same employee) is logged and the
/// loop continues — it never aborts the whole company or run. Every skip/failure is logged with
/// enough context (CompanyId/EmployeeId/PositionId) to be observable and actioned; a fresh
/// SaveChangesAsync per employee means a permanent failure for one employee can never re-occur for
/// every other employee behind it.
///
/// Invoked both from <see cref="IdentityModule.ReconcilePositionRoleAssignmentsAsync"/> (once at
/// startup, for immediate first-run coverage) and from the recurring
/// <see cref="Jobs.PositionRoleReconciliationJob"/> (every 15 minutes — see
/// IdentityModule.UseIdentityRecurringJobs) so a missed/failed sync recovers without requiring a
/// restart.
/// </summary>
internal sealed class PositionRoleReconciliationService(
    IdentityDbContext db,
    IEmployeeAudienceReader employeeAudienceReader,
    PositionSync positionSync,
    IClock clock,
    ILogger<PositionRoleReconciliationService> logger)
{
    public async Task ReconcileAllCompaniesAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var companyIds = await db.UserProfiles
            .Select(p => p.CompanyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var companyId in companyIds)
        {
            IReadOnlyList<Guid> employeeIds;
            try
            {
                employeeIds = await employeeAudienceReader.GetAllEmployeeIdsAsync(companyId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Position-role reconciliation could not list employees for company {CompanyId}; " +
                    "this company is skipped for this pass and will be retried on the next scheduled run.",
                    companyId);
                continue;
            }

            if (employeeIds.Count == 0)
                continue;

            IReadOnlyDictionary<Guid, EmployeeAudienceProfile> profiles;
            try
            {
                profiles = await employeeAudienceReader.GetEmployeeAudienceProfilesAsync(companyId, employeeIds, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Position-role reconciliation could not load employee profiles for company {CompanyId}; " +
                    "this company is skipped for this pass and will be retried on the next scheduled run.",
                    companyId);
                continue;
            }

            foreach (var (employeeId, profile) in profiles)
            {
                try
                {
                    await ReconcileEmployeeAsync(companyId, employeeId, profile.PositionProfileId, now, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Isolate this employee's failure from the rest of the run/company. Drop any
                    // partially-tracked/half-mutated state for this employee so the loop continues
                    // cleanly against a known-good context.
                    db.ChangeTracker.Clear();
                    logger.LogError(ex,
                        "Position-role reconciliation failed for employee {EmployeeId} in company {CompanyId}; " +
                        "will retry on the next scheduled run.",
                        employeeId, companyId);
                }
            }
        }
    }

    private async Task ReconcileEmployeeAsync(
        Guid companyId, Guid employeeId, Guid? currentPositionProfileId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var assignments = await db.UserPositions
            .Where(up => up.UserId == employeeId)
            .ToListAsync(cancellationToken);

        Position? currentPosition = null;
        if (currentPositionProfileId is { } positionId)
        {
            try
            {
                currentPosition = await positionSync.EnsureExistsAsync(companyId, positionId, now, cancellationToken);
            }
            catch (Exception ex)
            {
                // Resolving/provisioning the NEW position failed (deleted PositionProfile, or a
                // transient read failure). Isolate this failure here (rather than letting it
                // propagate to the employee-level catch in ReconcileAllCompaniesAsync) so the
                // revocation loop and SaveChangesAsync below still run this pass — an unresolved
                // new position never justifies retaining grants for a position the employee no
                // longer holds. No assignment can legitimately already exist for the unresolved
                // position (a UserPosition row only exists for a Position that has been synced), so
                // this can never suppress a legitimate reopen/insert below; the new assignment is
                // just retried on the next scheduled run.
                currentPosition = null;
                logger.LogWarning(ex,
                    "Position-role reconciliation could not resolve position {PositionProfileId} for employee " +
                    "{EmployeeId} in company {CompanyId}; stale assignments for other positions are still " +
                    "expired this pass, but the new assignment will be retried on the next scheduled run.",
                    positionId, employeeId, companyId);
            }

            if (currentPosition is null)
            {
                logger.LogInformation(
                    "Position-role reconciliation has no resolved position {PositionProfileId} for employee " +
                    "{EmployeeId} in company {CompanyId} this pass; stale assignments for other positions are " +
                    "still expired, and the new assignment will be retried on the next scheduled run.",
                    positionId, employeeId, companyId);
            }
        }

        var changed = false;

        // Converge: anything that is not the current position must not remain an active grant,
        // regardless of how it got left active (failed revocation, out-of-order/duplicate event
        // delivery, or a never-cleaned-up historical assignment).
        //
        // Classify against the authoritative currentPositionProfileId, NOT currentPosition — the
        // latter is null whenever EnsureExistsAsync fails or returns null this pass, but a
        // UserPosition/Position row for the (still authoritative) current position can already exist
        // from an earlier successful sync. Classifying against currentPosition here would treat that
        // already-active, still-correct assignment as stale and wrongly expire it whenever this
        // pass's position lookup merely fails to refresh it.
        foreach (var assignment in assignments)
        {
            var isCurrent = currentPositionProfileId is { } cpid && assignment.PositionId == cpid;
            if (!isCurrent && assignment.IsActive(now))
            {
                assignment.SetExpiry(now);
                changed = true;
            }
        }

        if (currentPosition is not null)
        {
            var currentAssignment = assignments.FirstOrDefault(a => a.PositionId == currentPosition.Id);
            if (currentAssignment is null)
            {
                db.UserPositions.Add(UserPosition.Create(employeeId, currentPosition.Id, now));
                changed = true;
            }
            else if (!currentAssignment.IsActive(now))
            {
                // Reopen rather than insert — (UserId, PositionId) is the composite primary key, so
                // a naive insert here would throw a duplicate-key DbUpdateException.
                currentAssignment.ClearExpiry();
                changed = true;
            }
        }

        if (!changed)
            return;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race against a concurrent write for the same employee (e.g. a live
            // OnEmployeePositionChanged event processing at the same moment, or an overlapping
            // reconciliation pass). Defer entirely to the next scheduled run rather than fail this
            // employee's whole reconciliation — the next pass re-reads current state and converges
            // again, so nothing is permanently lost.
            db.ChangeTracker.Clear();
            logger.LogWarning(
                "Position-role reconciliation lost a concurrent-write race for employee {EmployeeId} in company " +
                "{CompanyId}; will retry on the next scheduled run.",
                employeeId, companyId);
        }
    }
}
