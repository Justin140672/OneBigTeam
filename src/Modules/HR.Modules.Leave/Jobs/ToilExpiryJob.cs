using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Leave.Jobs;

/// <summary>
/// Daily job (LEAVE-06) that expires due TOIL for every company with at least one active,
/// expiry-configured TOIL leave type. Evaluated in the company's own time zone, mirroring
/// LeaveYearRolloverJob/ProcessLeavingEmployeesJob. Delegates the actual expiry work to
/// <see cref="ToilExpiryService"/>, which is independently idempotent, so re-running this job (or
/// Hangfire retrying it) for a company that has already been processed today is a safe no-op.
///
/// P1 follow-up (Ticket 4): each company is processed in its own DI scope - a fresh
/// <see cref="LeaveDbContext"/> and <see cref="ToilExpiryService"/> per iteration. Without this, a
/// company whose processing threw partway through leaves its faulted change tracker (partially
/// staged entities, a rolled-back-but-still-tracked balance, etc.) attached to the single DbContext
/// used for the rest of the loop, which can silently corrupt or short-circuit every subsequent
/// company's save. A fresh scope guarantees one company's failure can never leak state into the
/// next.
///
/// P1.1 follow-up: <see cref="ToilExpiryService.ExpireCompanyAsync"/> takes a row lock before
/// reading the TOIL ledger specifically to avoid stale-state concurrency failures, but as
/// defence-in-depth this job additionally retries a company up to <see cref="MaxAttemptsPerCompany"/>
/// times if a <see cref="DbUpdateConcurrencyException"/> still occurs. Each attempt gets its own
/// fresh DI scope (fresh DbContext, fresh transaction) and re-runs the entire calculation from
/// scratch against current data - it never retries a save against the stale, already-tracked
/// entities from a failed attempt. If every attempt is exhausted, the failure is logged clearly (as
/// an error, distinct from the warning logged for an individual retried attempt) so an incomplete
/// expiry for that company remains observable, and processing moves on to the next company
/// unaffected.
/// </summary>
internal sealed class ToilExpiryJob(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ICompanyTimeZoneReader companyTimeZoneReader,
    ILogger<ToilExpiryJob> logger)
{
    private const int MaxAttemptsPerCompany = 3;

    public async Task ExecuteAsync()
    {
        List<Guid> companyIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
            companyIds = await dbContext.LeaveTypes
                .Where(lt => lt.Behaviour == LeaveTypeBehaviour.Toil && lt.IsActive && lt.ToilExpiryDays != null)
                .Select(lt => lt.CompanyId)
                .Distinct()
                .ToListAsync();
        }

        foreach (var companyId in companyIds)
        {
            var timeZoneId = await companyTimeZoneReader.GetTimeZoneAsync(companyId, CancellationToken.None);
            var today = clock.TodayIn(timeZoneId);

            for (var attempt = 1; attempt <= MaxAttemptsPerCompany; attempt++)
            {
                try
                {
                    // Fresh scope every attempt, not just every company - a retried attempt must
                    // recompute from scratch against a brand new DbContext/transaction, never reuse
                    // the previous attempt's now-stale tracked entities.
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var expiryService = scope.ServiceProvider.GetRequiredService<ToilExpiryService>();

                    var result = await expiryService.ExpireCompanyAsync(companyId, today, CancellationToken.None);

                    if (result.TransactionsCreated > 0)
                    {
                        logger.LogInformation(
                            "TOIL expiry for company {CompanyId}: expired {Count} bucket(s) as of {AsOf}",
                            companyId,
                            result.TransactionsCreated,
                            today);
                    }

                    break; // success (including a legitimate no-op) - stop retrying this company.
                }
                catch (DbUpdateConcurrencyException ex) when (attempt < MaxAttemptsPerCompany)
                {
                    // The row lock inside ExpireCompanyAsync should make this rare, but if it still
                    // happens, the failed attempt's transaction was never committed (rolled back on
                    // dispose) - no partial ledger/balance changes and no audit event were
                    // persisted for it. Retry with a fresh scope rather than the stale one.
                    logger.LogWarning(
                        ex,
                        "TOIL expiry for company {CompanyId} hit a concurrency conflict on attempt {Attempt}/{MaxAttempts}; retrying with a fresh scope",
                        companyId,
                        attempt,
                        MaxAttemptsPerCompany);
                }
                catch (Exception ex)
                {
                    // Isolate one company's failure from the rest of the batch - ExpireCompanyAsync's
                    // own idempotency guard means a company that already completed is a safe no-op on
                    // retry, so only the failed company's remaining work is repeated. The scope (and
                    // its DbContext/change tracker) used for this company is disposed here regardless
                    // of outcome, so nothing from this attempt can leak into the next company's scope.
                    // This also catches a DbUpdateConcurrencyException on the final attempt, making
                    // retry exhaustion clearly observable as an error (distinct from the warnings
                    // logged for the earlier, retried attempts) rather than being silently swallowed.
                    logger.LogError(ex, "TOIL expiry failed for company {CompanyId} after {Attempt} attempt(s)", companyId, attempt);
                    break;
                }
            }
        }
    }
}
