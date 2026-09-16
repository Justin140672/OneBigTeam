using Hangfire;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Jobs;

/// <summary>
/// Ticket 7 (P2): deletes a purged candidate's uploaded document from blob storage. The owning
/// CandidateDocument row is removed synchronously (and durably) by PurgeEligibleCandidatesHandler
/// as part of the same database transaction as the Candidate redaction — this job only handles the
/// separate, potentially-failing external storage delete, so a storage outage never blocks or
/// partially-applies the purge itself, and a failed delete here is retried by Hangfire rather than
/// silently leaving an orphaned blob forever.
/// </summary>
[AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 30, 120, 600, 1800 })]
internal sealed class PurgeCandidateDocumentStorageJob(
    ICandidateDocumentStorageService storage,
    ILogger<PurgeCandidateDocumentStorageJob> logger)
{
    public async Task ProcessAsync(string storageKey)
    {
        try
        {
            await storage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "PurgeCandidateDocumentStorageJob: failed to delete storage key {StorageKey} — will retry.",
                storageKey);
            throw;
        }
    }
}
