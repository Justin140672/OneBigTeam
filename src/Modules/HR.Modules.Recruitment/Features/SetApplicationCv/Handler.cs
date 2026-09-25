using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.SetApplicationCv;

/// <summary>
/// Internal recruitment Ticket 1: attaches, replaces or removes the CV recorded as submitted with an
/// existing application. The referenced document must be a Kind = Cv CandidateDocument belonging to
/// the application's candidate in the same company (also enforced by the database's composite FK).
/// Uses the shared optimistic-concurrency helper so two simultaneous changes cannot silently
/// overwrite each other, and publishes an audit event for every actual change.
/// </summary>
internal sealed class SetApplicationCvHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    private const string ConflictMessage =
        "This application was changed by someone else since you opened it. Reload and try again.";

    public async Task<Result<SetApplicationCvResponse>> HandleAsync(
        SetApplicationCvRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Result.Failure<SetApplicationCvResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        // Reject stale callers up front, including for no-op requests, so the client always learns
        // its view of the application is out of date.
        if (request.ExpectedVersion is int expected && expected != application.Version)
            return Result.Failure<SetApplicationCvResponse>(Error.Concurrency(ConflictMessage));

        var now = clock.UtcNowOffset();
        var previousCvDocumentId = application.CvDocumentId;
        bool changed;

        if (request.CvDocumentId is Guid cvDocumentId)
        {
            var document = await db.CandidateDocuments
                .AsNoTracking()
                .SingleOrDefaultAsync(cd => cd.Id == cvDocumentId && cd.CompanyId == request.CompanyId, cancellationToken);

            var violation = document is null
                ? Application.CvDocumentNotFoundMessage
                : Application.DescribeCvDocumentViolation(document, application.CompanyId, application.CandidateId);

            if (violation is not null)
                return Result.Failure<SetApplicationCvResponse>(Error.Validation(violation));

            changed = application.AttachCv(document!, now);
        }
        else
        {
            changed = application.RemoveCv(now);
        }

        if (!changed)
            return Result.Success(ToResponse(application));

        var saveResult = await db.SaveChangesWithConcurrencyAsync(
            application, request.ExpectedVersion, ConflictMessage, cancellationToken);

        if (saveResult.IsFailure)
            return Result.Failure<SetApplicationCvResponse>(saveResult.Error);

        await auditPublisher.PublishAsync(
            new ApplicationCvReferenceChangedAuditEvent(
                application.CompanyId,
                application.Id,
                application.VacancyId,
                application.CandidateId,
                previousCvDocumentId,
                application.CvDocumentId,
                performedBy,
                now),
            cancellationToken);

        return Result.Success(ToResponse(application));
    }

    private static SetApplicationCvResponse ToResponse(Application application) => new(
        application.Id,
        application.VacancyId,
        application.CandidateId,
        application.CvDocumentId,
        application.Version,
        application.UpdatedAt);
}
