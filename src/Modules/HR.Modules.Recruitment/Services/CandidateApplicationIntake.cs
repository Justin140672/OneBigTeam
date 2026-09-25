using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Services;

/// <summary>Input to <see cref="CandidateApplicationIntake"/>. Tenant and actor are always resolved
/// server-side by the calling slice, never taken from the client body.</summary>
internal sealed record CandidateApplicationIntakeCommand(
    Guid CompanyId,
    Guid VacancyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? ResumeUrl,
    string? Notes,
    ApplicationSource? Source,
    Guid? SourceExternalRecruiterId,
    IFormFile? CvFile,
    Guid PerformedByUserId);

/// <summary>An existing candidate in the same company whose email matches (case-insensitively).</summary>
internal sealed record ExistingCandidateMatch(
    Guid CandidateId,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive);

internal sealed record CandidateApplicationIntakeCreated(
    Candidate Candidate,
    Application Application,
    CandidateDocument? CvDocument);

/// <summary>Exactly one of <see cref="Created"/>, <see cref="DuplicateCandidate"/> or
/// <see cref="Error"/> is set.</summary>
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
/// Duplicate email: there is no unique constraint on (company_id, email) — existing data is not
/// guaranteed to satisfy one (the legacy CreateCandidate check is case-sensitive). Instead, on
/// PostgreSQL the transaction takes a transaction-scoped advisory lock keyed on
/// (company, lower(email)) and re-checks for a match under the lock, so two concurrent intakes for the
/// same email serialise and the second one reports the first's candidate instead of creating another.
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

        // Same rule as CreateApplication (ticket #78): the recruiter must exist in this company; an
        // inactive recruiter is still a valid historical attribution.
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
            var fileValidation = CandidateDocumentUploadStaging.ValidateFile(
                cvFile.FileName, cvFile.ContentType, cvFile.Length, uploadOptions.Value);
            if (fileValidation.IsFailure)
                return CandidateApplicationIntakeOutcome.Failure(fileValidation.Error);
        }

        var email = command.Email.Trim();

        // Cheap pre-check outside the transaction so the common duplicate case never uploads a file.
        // Re-checked under the advisory lock below — this one alone is not race-safe.
        var existing = await FindExistingCandidateAsync(companyId, email, cancellationToken);
        if (existing is not null)
            return CandidateApplicationIntakeOutcome.Duplicate(existing);

        var now = clock.UtcNowOffset();

        // Defensive, as in CreateApplication: normally already seeded when the vacancy was created.
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
        ExistingCandidateMatch? raceWinner;

        try
        {
            var isRelational = db.Database.IsRelational();

            // Disposed (and therefore rolled back unless committed) when this try block exits,
            // before the catch below or the duplicate compensation runs.
            await using var transaction = isRelational
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            if (isRelational)
            {
                // Serialises concurrent intakes for the same company + email until commit/rollback.
                var lockKey = $"recruitment:candidate-email:{companyId:N}:{email.ToLowerInvariant()}";
                await db.Database.ExecuteSqlAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                    cancellationToken);
            }

            raceWinner = await FindExistingCandidateAsync(companyId, email, cancellationToken);
            if (raceWinner is null)
            {
                created = StageNewRecords(command, candidateId, email, initialStageId, staged, clock.UtcNowOffset());

                await db.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            // Clear the tracker so compensation's own save cannot re-attempt the failed inserts.
            db.ChangeTracker.Clear();
            if (staged is not null)
                await staging.CompensateAsync(staged);
            throw;
        }

        if (created is null)
        {
            // Another intake created a candidate with this email between the pre-check and the lock.
            db.ChangeTracker.Clear();
            if (staged is not null)
                await staging.CompensateAsync(staged);
            return CandidateApplicationIntakeOutcome.Duplicate(raceWinner!);
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
            command.ResumeUrl,
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

            // The intent is still tracked from the staging save; confirming it in this same save is
            // what marks the blob as owned.
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

    /// <summary>Case-insensitive match on the trimmed email within the company. Prefers an active
    /// record, then the oldest, if legacy data already holds more than one.</summary>
    private async Task<ExistingCandidateMatch?> FindExistingCandidateAsync(
        Guid companyId,
        string trimmedEmail,
        CancellationToken cancellationToken)
    {
        var normalised = trimmedEmail.ToLowerInvariant();

        return await db.Candidates
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Email.ToLower() == normalised)
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.CreatedAt)
            .Select(c => new ExistingCandidateMatch(c.Id, c.FirstName, c.LastName, c.Email, c.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
