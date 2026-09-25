using HR.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Support.Jobs;

/// <summary>
/// P1 stored-XSS fix: one-off, idempotent backfill that re-applies the shared support allow-list
/// (<see cref="HR.SharedKernel.Html.SupportHtmlSanitizer"/>) to every stored
/// <c>support.support_responses.body_html</c> value written before write-time sanitisation existed.
///
/// Safety properties:
/// <list type="bullet">
/// <item>Idempotent — sanitisation is stable under re-application, and only rows whose stored body
/// actually changes are updated, so re-runs (every deploy, manual triggers, concurrent instances)
/// converge on the same result and write nothing once the table is clean.</item>
/// <item>Bounded — only the id list is held in memory; bodies are loaded and saved in fixed-size
/// batches with the change tracker cleared between batches.</item>
/// <item>Cross-tenant by design — this is a system maintenance job with no ambient tenant; it touches
/// only the body column of rows it reads and never moves data between companies.</item>
/// <item>No content is logged — only row counts and response ids.</item>
/// </list>
///
/// Render-time sanitisation in HR.Web and HR.Admin.Web remains in place regardless, so historical
/// rows are safe to display even before this job has run.
/// </summary>
internal sealed class SupportResponseBodySanitisationJob(
    SupportDbContext db,
    ILogger<SupportResponseBodySanitisationJob> logger)
{
    internal const int BatchSize = 200;

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // Snapshot the ids first (16 bytes per row) and then load bodies chunk by chunk. Rows
        // inserted after the snapshot are already sanitised at write time, so they need no visit.
        var ids = await db.SupportResponses
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var scanned = 0;
        var updated = 0;

        foreach (var chunk in ids.Chunk(BatchSize))
        {
            var batch = await db.SupportResponses
                .AsTracking()
                .Where(r => chunk.Contains(r.Id))
                .ToListAsync(cancellationToken);

            foreach (var response in batch)
            {
                if (response.ResanitiseBody())
                {
                    updated++;
                    logger.LogInformation(
                        "SupportResponseBodySanitisationJob: re-sanitised stored body for support response {SupportResponseId}.",
                        response.Id);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();

            scanned += batch.Count;
        }

        logger.LogInformation(
            "SupportResponseBodySanitisationJob: scanned {ScannedCount} support responses, re-sanitised {UpdatedCount}.",
            scanned, updated);

        return updated;
    }
}
