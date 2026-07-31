using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

/// <summary>
/// Backs both IRecruitmentPipelineReader (OBT-709) and IVacancyPerformanceReader (OBT-710). Kept as
/// a single reader so the applicant/interview/offer/hire counting logic — which both reports need —
/// is written once. "Offers" are counted as distinct applications with an
/// ApplicationStageHistoryEntry into the company's "Offer" named RecruitmentStage (there is no
/// separate Offer entity/field in the domain). Date range filtering (when supplied) is applied
/// against Application.AppliedAt.
/// </summary>
internal sealed class RecruitmentReportReader(RecruitmentDbContext dbContext)
    : IRecruitmentPipelineReader, IVacancyPerformanceReader
{
    // Row cap (OBT-720 perf pass) — see HR.Modules.Sickness.Services.SicknessReportReader.MaxRows
    // for rationale. Applied to the raw applications query that both reports' metrics are built
    // from, bounding the per-vacancy aggregation fan-out for a company with an unusually large
    // application history.
    private const int MaxApplicationRows = 50_000;

    public async Task<IReadOnlyList<RecruitmentPipelineRecruiterRow>> GetByRecruiterAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        var metrics = await BuildVacancyMetricsAsync(companyId, startDate, endDate, cancellationToken);

        var vacancies = await dbContext.Vacancies
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Select(v => new { v.Id, v.AssignedRecruiterId })
            .ToListAsync(cancellationToken);

        var recruiterNames = await dbContext.ExternalRecruiters
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .ToDictionaryAsync(r => r.Id, r => r.AgencyName, cancellationToken);

        return vacancies
            .GroupBy(v => v.AssignedRecruiterId)
            .Select(g =>
            {
                var vacancyIds = g.Select(v => v.Id).ToHashSet();
                var rows = metrics.Where(m => vacancyIds.Contains(m.VacancyId)).ToList();

                return new RecruitmentPipelineRecruiterRow(
                    g.Key,
                    g.Key is not null && recruiterNames.TryGetValue(g.Key.Value, out var name) ? name : "Unassigned",
                    g.Count(),
                    rows.Sum(r => r.Applicants),
                    rows.Sum(r => r.Interviews),
                    rows.Sum(r => r.Offers),
                    rows.Sum(r => r.Hires));
            })
            .ToList();
    }

    public async Task<IReadOnlyList<RecruitmentPipelineVacancyRow>> GetByVacancyAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        var metrics = await BuildVacancyMetricsAsync(companyId, startDate, endDate, cancellationToken);

        var vacancyTitles = await dbContext.Vacancies
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Select(v => new { v.Id, v.AdvertTitle })
            .ToDictionaryAsync(v => v.Id, v => v.AdvertTitle, cancellationToken);

        return metrics
            .Select(m => new RecruitmentPipelineVacancyRow(
                m.VacancyId,
                vacancyTitles.TryGetValue(m.VacancyId, out var title) ? title ?? "(untitled vacancy)" : "(untitled vacancy)",
                m.Applicants,
                m.Interviews,
                m.Offers,
                m.Hires))
            .ToList();
    }

    public async Task<IReadOnlyList<VacancyPerformanceItem>> GetVacancyPerformanceAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        var metrics = await BuildVacancyMetricsAsync(companyId, startDate, endDate, cancellationToken);

        var vacancies = await dbContext.Vacancies
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Select(v => new { v.Id, v.AdvertTitle, v.OpenedAt, v.ClosedAt })
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var metricsByVacancy = metrics.ToDictionary(m => m.VacancyId);

        return vacancies
            .Select(v =>
            {
                var m = metricsByVacancy.GetValueOrDefault(v.Id);
                var daysOpen = v.OpenedAt is null
                    ? 0
                    : (v.ClosedAt ?? today).DayNumber - v.OpenedAt.Value.DayNumber;

                return new VacancyPerformanceItem(
                    v.Id,
                    v.AdvertTitle ?? "(untitled vacancy)",
                    v.OpenedAt,
                    v.ClosedAt,
                    Math.Max(daysOpen, 0),
                    m?.Applicants ?? 0,
                    m?.Interviews ?? 0,
                    m?.Offers ?? 0,
                    m?.HireDate);
            })
            .ToList();
    }

    private sealed record VacancyMetrics(
        Guid VacancyId, int Applicants, int Interviews, int Offers, int Hires, DateOnly? HireDate);

    private async Task<List<VacancyMetrics>> BuildVacancyMetricsAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        var applicationsQuery = dbContext.Applications
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId);

        if (startDate is not null)
        {
            var startInclusive = new DateTimeOffset(startDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            applicationsQuery = applicationsQuery.Where(a => a.AppliedAt >= startInclusive);
        }

        if (endDate is not null)
        {
            var endInclusive = new DateTimeOffset(endDate.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            applicationsQuery = applicationsQuery.Where(a => a.AppliedAt <= endInclusive);
        }

        var applications = await applicationsQuery
            .OrderBy(a => a.Id)
            .Take(MaxApplicationRows)
            .Select(a => new { a.Id, a.VacancyId })
            .ToListAsync(cancellationToken);

        var applicationIds = applications.Select(a => a.Id).ToHashSet();

        var interviewCounts = await dbContext.Interviews
            .AsNoTracking()
            .Where(i => i.CompanyId == companyId && applicationIds.Contains(i.ApplicationId))
            .GroupBy(i => i.ApplicationId)
            .Select(g => new { ApplicationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ApplicationId, x => x.Count, cancellationToken);

        var stages = await dbContext.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        var offerStageIds = stages
            .Where(s => s.Name.Equals("Offer", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Id)
            .ToHashSet();

        var hiredStageIds = stages
            .Where(s => s.TerminalOutcome == RecruitmentStageTerminalOutcome.Hired)
            .Select(s => s.Id)
            .ToHashSet();

        var offerEntries = offerStageIds.Count == 0
            ? []
            : await dbContext.ApplicationStageHistoryEntries
                .AsNoTracking()
                .Where(h => h.CompanyId == companyId && applicationIds.Contains(h.ApplicationId) && offerStageIds.Contains(h.NewStageId))
                .Select(h => h.ApplicationId)
                .Distinct()
                .ToListAsync(cancellationToken);
        var offeredApplicationIds = offerEntries.ToHashSet();

        var hireEntries = hiredStageIds.Count == 0
            ? []
            : await dbContext.ApplicationStageHistoryEntries
                .AsNoTracking()
                .Where(h => h.CompanyId == companyId && applicationIds.Contains(h.ApplicationId) && hiredStageIds.Contains(h.NewStageId))
                .GroupBy(h => h.ApplicationId)
                .Select(g => new { ApplicationId = g.Key, HiredAt = g.Max(h => h.ChangedAt) })
                .ToListAsync(cancellationToken);
        var hireDatesByApplication = hireEntries.ToDictionary(h => h.ApplicationId, h => h.HiredAt);

        return applications
            .GroupBy(a => a.VacancyId)
            .Select(g =>
            {
                var appIds = g.Select(a => a.Id).ToList();
                var hireDate = appIds
                    .Where(hireDatesByApplication.ContainsKey)
                    .Select(id => hireDatesByApplication[id])
                    .DefaultIfEmpty()
                    .Max();

                return new VacancyMetrics(
                    g.Key,
                    g.Count(),
                    appIds.Sum(id => interviewCounts.GetValueOrDefault(id, 0)),
                    appIds.Count(offeredApplicationIds.Contains),
                    appIds.Count(hireDatesByApplication.ContainsKey),
                    hireDate == default ? null : DateOnly.FromDateTime(hireDate.UtcDateTime));
            })
            .ToList();
    }
}
