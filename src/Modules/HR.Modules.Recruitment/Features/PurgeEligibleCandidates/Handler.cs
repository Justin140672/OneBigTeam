using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.PurgeEligibleCandidates;

/// <summary>
/// SET-05: "a separately authorised retention process can permanently redact eligible candidate
/// personal data" — mirrors Documents' PurgeEligibleArchivedEmployeeDocuments (DOC-04). Deliberately
/// not automatic: changing CandidateRetentionDays alone never destroys data (see
/// UpdateRecruitmentSettingsHandler) — only this explicit, company-administrator-gated action does,
/// and only for candidates that satisfy every eligibility condition below.
///
/// Eligible = not already purged, not linked to a hired employee, has no application sitting on a
/// non-terminal (still-in-progress) recruitment stage, and the candidate's last update is older than
/// the company's current CandidateRetentionDays window.
/// </summary>
internal sealed class PurgeEligibleCandidatesHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    ICompanyRecruitmentSettingsReader recruitmentSettingsReader,
    ILegalHoldStatusReader legalHoldStatusReader)
{
    public async Task<Result<PurgeEligibleCandidatesResponse>> HandleAsync(
        PurgeEligibleCandidatesRequest request,
        Guid purgedBy,
        CancellationToken cancellationToken)
    {
        // NFR-07: a company under legal hold is exempt from all retention deletion until lifted.
        if (await legalHoldStatusReader.IsUnderLegalHoldAsync(request.CompanyId, cancellationToken))
        {
            return Result.Failure<PurgeEligibleCandidatesResponse>(Error.Conflict(
                "This company is under a legal hold. Candidate purge is suspended until the hold is lifted."));
        }

        var now = clock.UtcNowOffset();
        var settings = await recruitmentSettingsReader.GetRecruitmentSettingsAsync(request.CompanyId, cancellationToken);
        var cutoff = now.AddDays(-settings.CandidateRetentionDays);

        var candidateIdsWithOpenApplications = db.Applications
            .Where(a => a.CompanyId == request.CompanyId && a.WithdrawnAt == null)
            .Join(db.RecruitmentStages.Where(s => !s.IsTerminal),
                a => a.CurrentStageId, s => s.Id, (a, s) => a.CandidateId)
            .Distinct();

        var eligibleCandidates = await db.Candidates
            .Where(c => c.CompanyId == request.CompanyId
                && c.PurgedAt == null
                && c.EmployeeId == null
                && c.UpdatedAt <= cutoff
                && !candidateIdsWithOpenApplications.Contains(c.Id))
            .ToListAsync(cancellationToken);

        if (eligibleCandidates.Count == 0)
            return Result.Success(new PurgeEligibleCandidatesResponse(0));

        foreach (var candidate in eligibleCandidates)
            candidate.Purge(purgedBy, now);

        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new CandidatesPurgedAuditEvent(
                request.CompanyId,
                eligibleCandidates.Select(c => c.Id).ToList(),
                purgedBy,
                now),
            cancellationToken);

        return Result.Success(new PurgeEligibleCandidatesResponse(eligibleCandidates.Count));
    }
}
