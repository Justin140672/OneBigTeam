using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Services;

internal sealed record CandidateApplicationIntakeCommand(
    Guid CompanyId,
    Guid VacancyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? Notes,
    ApplicationSource? Source,
    Guid? SourceExternalRecruiterId,
    IFormFile? CvFile,
    Guid PerformedByUserId);

internal sealed record CandidateApplicationIntakeCreated(
    Candidate Candidate,
    Application Application,
    CandidateDocument? CvDocument);

internal sealed class CandidateApplicationIntakeOutcome
{
    private CandidateApplicationIntakeOutcome() { }

    public CandidateApplicationIntakeCreated? Created { get; private init; }
    public ExistingCandidateMatch? DuplicateCandidate { get; private init; }
    public Error? Error { get; private init; }

    public static CandidateApplicationIntakeOutcome Success(CandidateApplicationIntakeCreated created) => new() { Created = created };
    public static CandidateApplicationIntakeOutcome Duplicate(ExistingCandidateMatch match) => new() { DuplicateCandidate = match };
    public static CandidateApplicationIntakeOutcome Failure(Error error) => new() { Error = error };
}

/// <summary>
/// Internal recruitment Ticket 3: the single coordinated use case that creates a new
/// <see cref="Candidate"/>, an optional CV <see cref="CandidateDocument"/> and their
/// <see cref="Application"/> to a vacancy. Owned by the Recruitment module; intended to be reused by
/// the employee self-apply slice (Ticket 4).
///
/// Consistency:
/// <list type="bullet">
/// <item><description>Candidate, CV document, application and the upload intent's confirmation are
/// written in ONE SaveChangesAsync inside one explicit transaction — all or nothing.</description></item>
/// <item><description>The CV blob is uploaded before that transaction via
/// <see cref="CandidateDocumentUploadStaging"/> (durable Reserved intent first). Any failure afterwards
/// — including a duplicate detected under the lock — rolls back, clears the change tracker and
/// compensates; the unconfirmed intent is the reconciliation backstop.</description></item>
/// </list>
///
/// Duplicate email: follows <see cref="CandidateEmailUniqueness"/>, shared with the legacy
/// CreateCandidate slice — compare on the normalised email, take the same transaction-scoped advisory
/// lock and re-check under it, so concurrent intakes (or an intake racing a legacy create) serialise
/// and the loser reports the winner's candidate. The unique index on (company_id, normalised_email) is
/// the final safeguard; a 23505 on it is reported as the same duplicate outcome.
/// </summary>
internal sealed class CandidateApplicationIntake(
    RecruitmentDbContext db,
    ICandidateDocumentStorageService storage,
    IOptions<CandidateDocumentUploadOptions> uploadOptions,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    RecruitmentStageSeeder stageSeeder,
    ILogger<CandidateApplicationIntake> logger,
    IExecutionContextAccessor? executionContextAccessor = null)
{
    public const string SubmittedCvTitle = "CV";

    public async Task<CandidateApplicationIntakeOutcome> CreateAsync(
        CandidateApplicationIntakeCommand command,
        CancellationToken cancellationToken)
    {
        var companyId = command.CompanyId;

        var vacancyExists = await db.Vacancies
            .AnyAsync(v => v.Id == command.VacancyId && v.CompanyId == companyId, cancellationToken);

        if (!vacancyExists)
            return CandidateApplicationIntakeOutcome.Failure(
                Error.NotFound($"Vacancy '{command.VacancyId}' was not found."));

        if (command.Source == ApplicationSource.ExternalRecruiter)
        {
            var recruiterExists = await db.ExternalRecruiters
                .AnyAsync(r => r.Id == command.SourceExternalRecruiterId && r.CompanyId == companyId, cancellationToken);

            if (!recruiterExists)
                return CandidateApplicationIntakeOutcome.Failure(
                    Error.NotFound($"External recruiter '{command.SourceExternalRecruiterId}' was not found."));
        }

        if (command.CvFile is { } cvFile)
        {
            var fileValidation = await CandidateDocumentUploadStaging.ValidateCvFileAsync(cvFile, uploadOptions.Value, cancellationToken);
            if (fileValidation.IsFailure)
                return CandidateApplicationIntakeOutcome.Failure(fileValidation.Error);
        }

        var email = command.Email.Trim();
        var normalisedEmail = CandidateEmail.Normalise(email);

        var existing = await CandidateEmailUniqueness.FindExistingAsync(db, companyId, normalisedEmail, cancellationToken);
        if (existing is not null)
            return CandidateApplicationIntakeOutcome.Duplicate(existing);

        var now = clock.UtcNowOffset();

        await stageSeeder.EnsureDefaultStagesSeededAsync(companyId, now, cancellationToken);

        var initialStageId = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.IsActive && !s.IsTerminal)
            .OrderBy(s => s.DisplayOrder)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (initialStageId == Guid.Empty)
            return CandidateApplicationIntakeOutcome.Failure(
                Error.Validation("This company has no active, non-terminal recruitment stage to place a new application on."));

        var candidateId = Guid.NewGuid();

        // The staging save below must not flush anything else — nothing else is tracked yet.
        var staging = new CandidateDocumentUploadStaging(db, storage, clock, logger, executionContextAccessor);
        StagedCandidateDocumentUpload? staged = null;
        if (command.CvFile is { } file)
            staged = await staging.ReserveAndUploadAsync(companyId, candidateId, file, cancellationToken);

        CandidateApplicationIntakeCreated? created = null;
        ExistingCandidateMatch? raceWinner = null;
        var hitUniqueIndex = false;

        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            await CandidateEmailUniqueness.AcquireCreationLockAsync(db, companyId, normalisedEmail, cancellationToken);

            raceWinner = await CandidateEmailUniqueness.FindExistingAsync(db, companyId, normalisedEmail, cancellationToken);
            if (raceWinner is null)
            {
                created = StageNewRecords(command, candidateId, email, initialStageId, staged, clock.UtcNowOffset());

                await db.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException ex) when (CandidateEmailUniqueness.IsViolation(ex))
        {
            created = null;
            hitUniqueIndex = true;
        }
        catch
        {
            db.ChangeTracker.Clear();
            if (staged is not null)
                await staging.CompensateAsync(staged);
            throw;
        }

        if (created is null)
        {
            db.ChangeTracker.Clear();
            if (staged is not null)
                await staging.CompensateAsync(staged);

            if (hitUniqueIndex)
            {
                logger.LogInformation(
                    "Candidate intake for vacancy {VacancyId} in company {CompanyId} hit the candidate email unique index; reporting the existing candidate.",
                    command.VacancyId, companyId);
                raceWinner = await CandidateEmailUniqueness.FindExistingAsync(db, companyId, normalisedEmail, cancellationToken);
            }

            return raceWinner is not null
                ? CandidateApplicationIntakeOutcome.Duplicate(raceWinner)
                : CandidateApplicationIntakeOutcome.Failure(
                    Error.Conflict("A candidate with this email already exists in this company."));
        }

        await PublishAuditAsync(created, command, cancellationToken);

        logger.LogInformation(
            "Candidate {CandidateId} and application {ApplicationId} created for vacancy {VacancyId} in company {CompanyId} (CV attached: {CvAttached}).",
            created.Candidate.Id, created.Application.Id, created.Application.VacancyId, companyId, created.CvDocument is not null);

        return CandidateApplicationIntakeOutcome.Success(created);
    }

    private CandidateApplicationIntakeCreated StageNewRecords(
        CandidateApplicationIntakeCommand command,
        Guid candidateId,
        string email,
        Guid initialStageId,
        StagedCandidateDocumentUpload? staged,
        DateTimeOffset now)
    {
        var candidate = Candidate.Create(
            candidateId,
            command.CompanyId,
            command.FirstName,
            command.LastName,
            email,
            command.Phone,
            now);
        db.Candidates.Add(candidate);

        CandidateDocument? cvDocument = null;
        if (staged is not null)
        {
            cvDocument = CandidateDocument.Create(
                Guid.NewGuid(),
                command.CompanyId,
                candidateId,
                SubmittedCvTitle,
                staged.FileName,
                staged.FileSize,
                staged.ContentType,
                staged.StorageKey,
                command.PerformedByUserId,
                now,
                CandidateDocumentKind.Cv);
            db.CandidateDocuments.Add(cvDocument);

            staged.Intent.MarkConfirmed(now);
        }

        var application = Application.Create(
            Guid.NewGuid(),
            command.CompanyId,
            command.VacancyId,
            candidateId,
            initialStageId,
            command.Notes,
            now,
            command.Source,
            command.SourceExternalRecruiterId);

        if (cvDocument is not null)
            application.AttachCv(cvDocument, now);

        db.Applications.Add(application);

        return new CandidateApplicationIntakeCreated(candidate, application, cvDocument);
    }

    private async Task PublishAuditAsync(
        CandidateApplicationIntakeCreated created,
        CandidateApplicationIntakeCommand command,
        CancellationToken cancellationToken)
    {
        var application = created.Application;

        if (application.CvDocumentId is Guid cvDocumentId)
        {
            await auditPublisher.PublishAsync(
                new ApplicationCvReferenceChangedAuditEvent(
                    application.CompanyId,
                    application.Id,
                    application.VacancyId,
                    application.CandidateId,
                    PreviousCvDocumentId: null,
                    NewCvDocumentId: cvDocumentId,
                    command.PerformedByUserId,
                    application.CreatedAt),
                cancellationToken);
        }

        if (application.Source is not null)
        {
            await auditPublisher.PublishAsync(
                new ApplicationSourceSetAuditEvent(
                    application.CompanyId,
                    application.Id,
                    application.VacancyId,
                    application.CandidateId,
                    application.Source.Value,
                    application.SourceExternalRecruiterId,
                    application.CreatedAt),
                cancellationToken);
        }
    }
}
