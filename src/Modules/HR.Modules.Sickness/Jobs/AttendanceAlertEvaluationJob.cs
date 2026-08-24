using HR.Modules.Companies.Contracts;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Jobs;

/// <summary>
/// Daily job (SICK-04) that runs the deterministic <see cref="AttendanceAlertEvaluationService"/>
/// against every employee with sickness history, per company, and persists any newly-firing rule as
/// an <see cref="AttendanceAlert"/>. Mirrors FitNoteRequestJob/ReturnToWorkReminderJob's shape:
/// company-scoped batches, evaluated against "today".
///
/// Entirely idempotent: before inserting, each candidate is checked against existing alerts for the
/// same employee+rule+evidence window (also enforced by a unique database index — see
/// AttendanceAlertConfiguration), so re-running this job — including a Hangfire retry after a
/// partial failure — never creates duplicate alerts. Purely additive: this job only ever inserts
/// AttendanceAlert rows; it never mutates SicknessRecord, ReturnToWorkReview, employment or
/// disciplinary state.
/// </summary>
internal sealed class AttendanceAlertEvaluationJob(
    SicknessDbContext db,
    ICompanySicknessSettingsReader sicknessSettingsReader,
    AttendanceAlertEvaluationService evaluationService,
    IClock clock)
{
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var companyIds = await db.SicknessRecords
            .AsNoTracking()
            .Select(r => r.CompanyId)
            .Distinct()
            .ToListAsync();

        foreach (var companyId in companyIds)
        {
            await EvaluateCompanyAsync(companyId, today, now);
        }
    }

    private async Task EvaluateCompanyAsync(Guid companyId, DateOnly today, DateTimeOffset now)
    {
        var settings = await sicknessSettingsReader.GetSicknessSettingsAsync(companyId, CancellationToken.None);

        var records = await db.SicknessRecords
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .ToListAsync();

        var reviews = await db.ReturnToWorkReviews
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .ToListAsync();

        var existingAlertKeys = await db.AttendanceAlerts
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId)
            .Select(a => new { a.EmployeeId, a.Rule, a.EvidencePeriodStart, a.EvidencePeriodEnd })
            .ToListAsync();

        var existingKeySet = existingAlertKeys
            .Select(k => (k.EmployeeId, k.Rule, k.EvidencePeriodStart, k.EvidencePeriodEnd))
            .ToHashSet();

        var changed = false;

        foreach (var employeeId in records.Select(r => r.EmployeeId).Distinct())
        {
            var employeeRecords = records.Where(r => r.EmployeeId == employeeId).ToList();
            var employeeReviews = reviews.Where(r => r.EmployeeId == employeeId).ToList();

            var candidates = evaluationService.Evaluate(employeeRecords, employeeReviews, settings, today);

            foreach (var candidate in candidates)
            {
                var key = (employeeId, candidate.Rule, candidate.EvidencePeriodStart, candidate.EvidencePeriodEnd);
                if (!existingKeySet.Add(key))
                    continue; // duplicate of an existing alert or of another candidate this run — skip.

                db.AttendanceAlerts.Add(AttendanceAlert.Create(
                    Guid.NewGuid(),
                    companyId,
                    employeeId,
                    candidate.Rule,
                    candidate.EvidencePeriodStart,
                    candidate.EvidencePeriodEnd,
                    candidate.OccurrenceCount,
                    candidate.Description,
                    now));

                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }
}
