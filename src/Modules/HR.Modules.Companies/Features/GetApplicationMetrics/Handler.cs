using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.GetApplicationMetrics;

/// <summary>
/// Platform-wide (not scoped to one customer) Application Metrics dashboard (Platform Monitoring
/// epic). Same defense-in-depth allow-list gate as GetSystemHealthHandler/ListBackgroundJobsHandler
/// (see their remarks) — no first-class platform-administrator identity model exists yet.
///
/// Combines real historical data (signups, document uploads — grouped by day directly from source
/// tables) with a daily append-only snapshot (PlatformMetricsSnapshot) for metrics that have no
/// other historical record (active companies/users, storage, cumulative succeeded jobs). The
/// snapshot is written at most once per calendar day, on-demand, the first time an admin views this
/// dashboard that day.
/// </summary>
internal sealed class GetApplicationMetricsHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IPlatformDocumentActivityReader documentActivityReader,
    IPlatformUserActivityReader userActivityReader,
    IBackgroundJobStatusReader backgroundJobStatusReader)
{
    public async Task<Result<GetApplicationMetricsResponse>> HandleAsync(
        GetApplicationMetricsRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<GetApplicationMetricsResponse>(
                Error.Unauthorized("This account is not authorised to view platform metrics data."));
        }

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var fromDate = today.AddDays(-29);

        var fromDateTimeOffset = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var companies = await dbContext.Companies
            .AsNoTracking()
            .Where(c => c.CreatedAt >= fromDateTimeOffset)
            .Select(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        var signupsByDate = companies
            .Select(c => DateOnly.FromDateTime(c.UtcDateTime.Date))
            .Where(date => date >= fromDate && date <= today)
            .GroupBy(date => date)
            .ToDictionary(g => g.Key, g => g.Count());

        var dailySignups = FillGaps(signupsByDate, fromDate, today);

        var documentActivity = await documentActivityReader.GetPlatformActivityAsync(
            fromDate, today, cancellationToken);

        var uploadsByDate = documentActivity.DailyUploads
            .ToDictionary(d => d.Date, d => d.Count);

        var dailyDocumentsUploaded = FillGaps(uploadsByDate, fromDate, today);

        var activeCompanies = await dbContext.CustomerSubscriptions
            .AsNoTracking()
            .CountAsync(s => s.Status != SubscriptionStatus.Canceled, cancellationToken);

        var activeUsers = await userActivityReader.GetTotalUserCountAsync(cancellationToken);

        var storageConsumedBytes = documentActivity.TotalStorageBytes;

        var backgroundJobsSucceededTotal = backgroundJobStatusReader.GetStatus().Succeeded;

        var existingSnapshot = await dbContext.PlatformMetricsSnapshots
            .FirstOrDefaultAsync(s => s.SnapshotDate == today, cancellationToken);

        if (existingSnapshot is null)
        {
            dbContext.PlatformMetricsSnapshots.Add(PlatformMetricsSnapshot.Create(
                Guid.NewGuid(), today, DateTimeOffset.UtcNow,
                activeCompanies, activeUsers, storageConsumedBytes, backgroundJobsSucceededTotal));

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var activeCompaniesTrend = await dbContext.PlatformMetricsSnapshots
            .AsNoTracking()
            .Where(s => s.SnapshotDate >= fromDate)
            .OrderBy(s => s.SnapshotDate)
            .Select(s => new DailyMetricPoint(s.SnapshotDate, s.ActiveCompanies))
            .ToListAsync(cancellationToken);

        var response = new GetApplicationMetricsResponse(
            dailySignups,
            dailyDocumentsUploaded,
            activeCompaniesTrend,
            activeCompanies,
            activeUsers,
            storageConsumedBytes,
            backgroundJobsSucceededTotal,
            EmailsSentTracked: false,
            EmailsSentGapReason: "Email sends are not currently persisted to a send-record/outbox table, so no count is available. Tracked as a known platform gap.");

        return Result.Success(response);
    }

    private static IReadOnlyList<DailyMetricPoint> FillGaps(
        IReadOnlyDictionary<DateOnly, int> countsByDate, DateOnly fromDate, DateOnly toDate)
    {
        var points = new List<DailyMetricPoint>();
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            points.Add(new DailyMetricPoint(date, countsByDate.GetValueOrDefault(date)));
        }

        return points;
    }

    private bool IsAllowListedPlatformAdmin()
    {
        var email = currentUser.Email;
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var allowedEmails = configuration.GetSection("PlatformAdmin:AllowedEmails").Get<string[]>()
            ?? [];

        return allowedEmails.Any(allowed =>
            string.Equals(allowed, email, StringComparison.OrdinalIgnoreCase));
    }
}
