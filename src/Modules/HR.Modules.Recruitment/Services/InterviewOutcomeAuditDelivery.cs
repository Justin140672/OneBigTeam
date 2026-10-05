using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Services;

internal sealed class InterviewOutcomeAuditDelivery(
    RecruitmentDbContext db,
    IAuditEventPublisher auditPublisher,
    IAuditEventExistenceReader auditExistenceReader,
    IClock clock)
{
    /// <summary>
    /// Marks delivery complete only once the deterministic audit event (EventId = interview id) is
    /// seen to exist. The publisher logs and swallows persistence failures, so a normal return from
    /// it proves nothing; an unconfirmed event throws and leaves delivery outstanding for retry.
    /// Missing interview/application data throws <see cref="InterviewOutcomeSourceDataMissingException"/>
    /// and never marks delivery complete.
    /// </summary>
    public async Task DeliverAsync(InterviewOutcomeTaskReconciliation record, CancellationToken cancellationToken)
    {
        if (record.AuditDeliveredAt is not null)
            return;

        if (!await auditExistenceReader.ExistsAsync(record.InterviewId, cancellationToken))
        {
            var details = await (
                from i in db.Interviews.AsNoTracking()
                join a in db.Applications.AsNoTracking() on i.ApplicationId equals a.Id
                where i.Id == record.InterviewId && i.CompanyId == record.CompanyId
                select new { i.Outcome, i.Notes, a.VacancyId, a.CandidateId })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new InterviewOutcomeSourceDataMissingException(
                    record.CompanyId, record.InterviewId, record.ApplicationId);

            await auditPublisher.PublishAsync(
                new InterviewOutcomeRecordedAuditEvent(
                    record.CompanyId,
                    record.InterviewId,
                    record.ApplicationId,
                    details.VacancyId,
                    details.CandidateId,
                    details.Outcome,
                    details.Notes,
                    record.RecordedBy,
                    record.CreatedAt),
                cancellationToken);

            if (!await auditExistenceReader.ExistsAsync(record.InterviewId, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"The interview-outcome audit event for interview {record.InterviewId} was not confirmed as persisted.");
            }
        }

        record.MarkAuditDelivered(clock.UtcNowOffset());
        await db.SaveChangesAsync(cancellationToken);
    }
}
