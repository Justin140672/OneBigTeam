using HR.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Support.Jobs;

internal sealed class SupportResponseBodySanitisationJob(
    SupportDbContext db,
    ILogger<SupportResponseBodySanitisationJob> logger)
{
    internal const int BatchSize = 200;

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
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
