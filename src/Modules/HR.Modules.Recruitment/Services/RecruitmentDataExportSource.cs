using System.Globalization;
using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

/// <summary>
/// Story 2: contributes the Recruitment module's principal data (vacancies, candidates,
/// applications, interviews) to the organisation data export. Offers are represented by the
/// offer-approval fields already carried on the application row. company_id enforced on every query.
/// </summary>
internal sealed class RecruitmentDataExportSource(RecruitmentDbContext db) : IRecruitmentDataExportSource
{
    public async Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var vacancies = await db.Vacancies.AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Select(v => new { v.Id, v.PositionProfileId, v.AdvertTitle, v.Status, v.HiringManagerId, v.AssignedRecruiterId, v.OpenedAt, v.ClosedAt, v.CreatedAt })
            .ToListAsync(cancellationToken);

        var vacanciesTable = new DataExportTable(
            "vacancies",
            ["Id", "PositionProfileId", "AdvertTitle", "Status", "HiringManagerId", "AssignedRecruiterId", "OpenedAt", "ClosedAt", "CreatedAt"],
            vacancies.Select(v => (IReadOnlyList<string?>)new string?[]
            {
                v.Id.ToString(), v.PositionProfileId.ToString(), v.AdvertTitle, v.Status.ToString(),
                v.HiringManagerId.ToString(), v.AssignedRecruiterId?.ToString(), D(v.OpenedAt), D(v.ClosedAt), T(v.CreatedAt)
            }).ToList());

        var candidates = await db.Candidates.AsNoTracking()
            .Where(c => c.CompanyId == companyId)
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.EmployeeId, c.IsActive, c.CreatedAt })
            .ToListAsync(cancellationToken);

        var candidatesTable = new DataExportTable(
            "candidates",
            ["Id", "FirstName", "LastName", "Email", "Phone", "EmployeeId", "IsActive", "CreatedAt"],
            candidates.Select(c => (IReadOnlyList<string?>)new string?[]
            {
                c.Id.ToString(), c.FirstName, c.LastName, c.Email, c.Phone, c.EmployeeId?.ToString(),
                c.IsActive ? "true" : "false", T(c.CreatedAt)
            }).ToList());

        var applications = await db.Applications.AsNoTracking()
            .Where(a => a.CompanyId == companyId)
            .Select(a => new { a.Id, a.VacancyId, a.CandidateId, a.CurrentStageId, a.InterviewOutcome, a.RejectionReason, a.WithdrawnAt, a.OfferApprovedAt, a.AppliedAt })
            .ToListAsync(cancellationToken);

        var applicationsTable = new DataExportTable(
            "applications",
            ["Id", "VacancyId", "CandidateId", "CurrentStageId", "InterviewOutcome", "RejectionReason", "WithdrawnAt", "OfferApprovedAt", "AppliedAt"],
            applications.Select(a => (IReadOnlyList<string?>)new string?[]
            {
                a.Id.ToString(), a.VacancyId.ToString(), a.CandidateId.ToString(), a.CurrentStageId.ToString(),
                a.InterviewOutcome?.ToString(), a.RejectionReason, T(a.WithdrawnAt), T(a.OfferApprovedAt), T(a.AppliedAt)
            }).ToList());

        var interviews = await db.Interviews.AsNoTracking()
            .Where(i => i.CompanyId == companyId)
            .Select(i => new { i.Id, i.ApplicationId, i.InterviewerEmployeeId, i.ScheduledAt, i.DurationMinutes, i.Location, i.Outcome, i.CreatedAt })
            .ToListAsync(cancellationToken);

        var interviewsTable = new DataExportTable(
            "interviews",
            ["Id", "ApplicationId", "InterviewerEmployeeId", "ScheduledAt", "DurationMinutes", "Location", "Outcome", "CreatedAt"],
            interviews.Select(i => (IReadOnlyList<string?>)new string?[]
            {
                i.Id.ToString(), i.ApplicationId.ToString(), i.InterviewerEmployeeId.ToString(), T(i.ScheduledAt),
                i.DurationMinutes?.ToString(CultureInfo.InvariantCulture), i.Location, i.Outcome.ToString(), T(i.CreatedAt)
            }).ToList());

        return [vacanciesTable, candidatesTable, applicationsTable, interviewsTable];
    }

    private static string? D(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string T(DateTimeOffset value) => value.ToString("o", CultureInfo.InvariantCulture);
    private static string? T(DateTimeOffset? value) => value?.ToString("o", CultureInfo.InvariantCulture);
}
