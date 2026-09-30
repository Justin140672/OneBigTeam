using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HR.Modules.Recruitment.Features.ApplyForInternalVacancy;

internal sealed record ApplyForInternalVacancyResult(
    Result<ApplyForInternalVacancyResponse> Result,
    ApplyForInternalVacancyRejection? Rejection)
{
    public static ApplyForInternalVacancyResult Success(ApplyForInternalVacancyResponse response) =>
        new(HR.SharedKernel.Result.Success(response), null);

    public static ApplyForInternalVacancyResult Failed(Error error) =>
        new(HR.SharedKernel.Result.Failure<ApplyForInternalVacancyResponse>(error), null);

    public static ApplyForInternalVacancyResult Rejected(int statusCode, string code, string message) =>
        new(HR.SharedKernel.Result.Failure<ApplyForInternalVacancyResponse>(new Error(code, message)),
            new ApplyForInternalVacancyRejection(statusCode, code, message));

    public static ApplyForInternalVacancyResult AlreadyApplied() => Rejected(
        StatusCodes.Status409Conflict,
        ApplyForInternalVacancyRejection.AlreadyAppliedCode,
        "You have already applied for this vacancy.");

    public static ApplyForInternalVacancyResult NotEligible() => Rejected(
        StatusCodes.Status403Forbidden,
        ApplyForInternalVacancyRejection.NotEligibleCode,
        "Only current employees can apply for internal vacancies.");

    public static ApplyForInternalVacancyResult EmailInUse() => Rejected(
        StatusCodes.Status409Conflict,
        ApplyForInternalVacancyRejection.EmailInUseCode,
        "Your work email address is already used by another candidate record. Please contact HR so they can resolve it before you apply.");
}

/// <summary>
/// Internal recruitment Ticket 4: the signed-in employee applies for an internally advertised vacancy.
///
/// Eligibility: the applicant is always the authenticated user (UserId == EmployeeId convention), looked
/// up in the route company through <see cref="IEmployeeApplicantReader"/>; only an employee whose
/// employment state is <see cref="EmployeeApplicantEmploymentState.Active"/> may apply (Draft, Suspended,
/// Leaving and Former are refused). The vacancy must belong to the same company, be Open and be
/// advertised internally — anything else is "not found", exactly like GetInternalVacancy.
///
/// Candidate: the employee's single linked candidate (unique per company + employee) is reused, with
/// its name/email/phone refreshed from the Employee record and reactivated if a recruiter had
/// deactivated it; otherwise one is created with <see cref="Candidate.CreateForEmployee"/>. An external
/// candidate that merely shares the employee's email is never taken over — candidate emails are unique
/// per company, so that case is refused with <c>applicant_email_in_use</c> for HR to resolve.
///
/// Consistency and concurrency (mirrors CandidateApplicationIntake):
/// <list type="bullet">
/// <item><description>The CV is uploaded first through <see cref="CandidateDocumentUploadStaging"/> (durable
/// Reserved intent before any bytes), then the candidate, CV document, application and the intent's
/// confirmation are written in ONE SaveChangesAsync inside one transaction. Any refusal or failure after
/// the upload clears the tracker and compensates; the unconfirmed intent is the reconciliation backstop,
/// so no uploaded file is ever left untracked.</description></item>
/// <item><description>The transaction takes an advisory lock on (company, employee) — serialising this
/// employee's concurrent applications — and the shared candidate-email creation lock, then re-checks
/// everything under the locks. The unique indexes on candidates (company_id, employee_id),
/// candidates (company_id, normalised_email) and applications (vacancy_id, candidate_id) are the final
/// database-level safeguards; their violations map to the same refusals.</description></item>
/// <item><description>If the employee's linked candidate appeared concurrently (so the CV was staged under a
/// different candidate id), the attempt is compensated and retried once against that candidate.</description></item>
/// </list>
/// </summary>
internal sealed class ApplyForInternalVacancyHandler(
    RecruitmentDbContext db,
    IEmployeeApplicantReader applicantReader,
    ICandidateDocumentStorageService storage,
    IOptions<CandidateDocumentUploadOptions> uploadOptions,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    RecruitmentStageSeeder stageSeeder,
    ILogger<ApplyForInternalVacancyHandler> logger,
    IExecutionContextAccessor? executionContextAccessor = null)
{
    internal const string CandidateEmployeeUniqueIndexName = "ix_candidates_company_id_employee_id";
    internal const string ApplicationVacancyCandidateUniqueIndexName = "IX_applications_vacancy_id_candidate_id";

    private const int MaxAttempts = 2;

    private const string PurgedCandidateMessage =
        "Your previous recruitment record can no longer be used for new applications. Please contact HR.";

    private const string ConcurrentChangeMessage =
        "Your application could not be completed because your recruitment record changed at the same time. Please try again.";

    private sealed record LinkedCandidateInfo(Guid Id, DateTimeOffset? PurgedAt);

    private sealed record StagedApplication(
        Candidate Candidate,
        Application Application,
        CandidateDocument CvDocument,
        bool CandidateCreated,
        bool CandidateIdentityRefreshed,
        bool CandidateReactivated,
        CandidateAuditSnapshot? IdentityBefore);

    public async Task<ApplyForInternalVacancyResult> HandleAsync(
        ApplyForInternalVacancyRequest request,
        Guid applicantEmployeeId,
        CancellationToken cancellationToken)
    {
        var companyId = request.CompanyId;

        var applicant = await applicantReader.GetApplicantAsync(companyId, applicantEmployeeId, cancellationToken);
        if (applicant is null || applicant.EmploymentState != EmployeeApplicantEmploymentState.Active)
            return ApplyForInternalVacancyResult.NotEligible();

        var vacancyIsOpenInternally = await db.Vacancies
            .AsNoTracking()
            .AnyAsync(
                v => v.Id == request.VacancyId
                    && v.CompanyId == companyId
                    && v.Status == VacancyStatus.Open
                    && v.IsAdvertisedInternally,
                cancellationToken);

        if (!vacancyIsOpenInternally)
            return ApplyForInternalVacancyResult.Failed(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        var identityViolation = Candidate.DescribeEmployeeIdentityViolation(
            applicant.FirstName, applicant.LastName, applicant.WorkEmail);
        if (identityViolation is not null)
            return ApplyForInternalVacancyResult.Failed(Error.Validation(identityViolation));

        if (request.CvFile is not { } cvFile)
            return ApplyForInternalVacancyResult.Failed(Error.Validation("A CV file is required to apply."));

        var fileValidation = CandidateDocumentUploadStaging.ValidateFile(
            cvFile.FileName, cvFile.ContentType, cvFile.Length, uploadOptions.Value);
        if (fileValidation.IsFailure)
            return ApplyForInternalVacancyResult.Failed(fileValidation.Error);

        await stageSeeder.EnsureDefaultStagesSeededAsync(companyId, clock.UtcNowOffset(), cancellationToken);

        var initialStageId = await db.RecruitmentStages
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.IsActive && !s.IsTerminal)
            .OrderBy(s => s.DisplayOrder)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (initialStageId == Guid.Empty)
            return ApplyForInternalVacancyResult.Failed(
                Error.Validation("This vacancy is not currently accepting applications."));

        for (var attempt = 1; ; attempt++)
        {
            var outcome = await TryApplyAsync(request.VacancyId, applicant, cvFile, initialStageId, cancellationToken);
            if (outcome is not null)
                return outcome;

            if (attempt >= MaxAttempts)
                return ApplyForInternalVacancyResult.Failed(Error.Concurrency(ConcurrentChangeMessage));

            logger.LogInformation(
                "Internal application by employee {EmployeeId} for vacancy {VacancyId} in company {CompanyId}: linked candidate changed concurrently; retrying (attempt {Attempt}).",
                applicant.EmployeeId, request.VacancyId, companyId, attempt + 1);
        }
    }

    private async Task<ApplyForInternalVacancyResult?> TryApplyAsync(
        Guid vacancyId,
        EmployeeApplicantProfile applicant,
        IFormFile cvFile,
        Guid initialStageId,
        CancellationToken cancellationToken)
    {
        var companyId = applicant.CompanyId;
        var employeeId = applicant.EmployeeId;
        var normalisedEmail = CandidateEmail.Normalise(applicant.WorkEmail);

        var linked = await db.Candidates
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.EmployeeId == employeeId)
            .Select(c => new LinkedCandidateInfo(c.Id, c.PurgedAt))
            .SingleOrDefaultAsync(cancellationToken);

        var refusal = await CheckRefusalsAsync(companyId, vacancyId, linked, normalisedEmail, cancellationToken);
        if (refusal is not null)
            return refusal;

        // The staging save below must not flush anything else — nothing else is pending.
        var candidateId = linked?.Id ?? Guid.NewGuid();
        var staging = new CandidateDocumentUploadStaging(db, storage, clock, logger, executionContextAccessor);
        var staged = await staging.ReserveAndUploadAsync(companyId, candidateId, cvFile, cancellationToken);

        StagedApplication? created = null;
        var retry = false;

        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            await AcquireEmployeeLockAsync(companyId, employeeId, cancellationToken);
            await CandidateEmailUniqueness.AcquireCreationLockAsync(db, companyId, normalisedEmail, cancellationToken);

            var candidate = await db.Candidates
                .SingleOrDefaultAsync(c => c.CompanyId == companyId && c.EmployeeId == employeeId, cancellationToken);

            if (candidate?.Id != linked?.Id)
            {
                retry = true;
            }
            else
            {
                refusal = await CheckRefusalsAsync(
                    companyId,
                    vacancyId,
                    candidate is null ? null : new LinkedCandidateInfo(candidate.Id, candidate.PurgedAt),
                    normalisedEmail,
                    cancellationToken);

                if (refusal is null)
                {
                    created = StageRecords(vacancyId, applicant, candidate, candidateId, initialStageId, staged, clock.UtcNowOffset());

                    await db.SaveChangesAsync(cancellationToken);

                    if (transaction is not null)
                        await transaction.CommitAsync(cancellationToken);
                }
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            created = null;
            refusal = ApplyForInternalVacancyResult.Failed(Error.Concurrency(ConcurrentChangeMessage));
        }
        catch (DbUpdateException ex) when (CandidateEmailUniqueness.IsViolation(ex))
        {
            created = null;
            refusal = ApplyForInternalVacancyResult.EmailInUse();
        }
        catch (DbUpdateException ex) when (IsUniqueViolationOn(ex, ApplicationVacancyCandidateUniqueIndexName))
        {
            created = null;
            refusal = ApplyForInternalVacancyResult.AlreadyApplied();
        }
        catch (DbUpdateException ex) when (IsUniqueViolationOn(ex, CandidateEmployeeUniqueIndexName))
        {
            created = null;
            retry = true;
        }
        catch
        {
            db.ChangeTracker.Clear();
            await staging.CompensateAsync(staged);
            throw;
        }

        if (created is null)
        {
            db.ChangeTracker.Clear();
            await staging.CompensateAsync(staged);
            return retry ? null : refusal;
        }

        await PublishAuditAsync(created, employeeId, cancellationToken);

        var application = created.Application;

        logger.LogInformation(
            "Employee {EmployeeId} applied for internal vacancy {VacancyId} in company {CompanyId}: application {ApplicationId}, candidate {CandidateId} (created: {CandidateCreated}, reactivated: {CandidateReactivated}).",
            employeeId, application.VacancyId, companyId, application.Id, created.Candidate.Id,
            created.CandidateCreated, created.CandidateReactivated);

        return ApplyForInternalVacancyResult.Success(new ApplyForInternalVacancyResponse(
            application.Id,
            application.CompanyId,
            application.VacancyId,
            created.Candidate.Id,
            created.CvDocument.Id,
            ApplicationSource.Internal,
            application.AppliedAt));
    }

    private async Task<ApplyForInternalVacancyResult?> CheckRefusalsAsync(
        Guid companyId,
        Guid vacancyId,
        LinkedCandidateInfo? linked,
        string normalisedEmail,
        CancellationToken cancellationToken)
    {
        if (linked is not null)
        {
            var alreadyApplied = await db.Applications
                .AsNoTracking()
                .AnyAsync(a => a.CompanyId == companyId && a.VacancyId == vacancyId && a.CandidateId == linked.Id, cancellationToken);

            if (alreadyApplied)
                return ApplyForInternalVacancyResult.AlreadyApplied();

            if (linked.PurgedAt is not null)
                return ApplyForInternalVacancyResult.Failed(Error.Conflict(PurgedCandidateMessage));
        }

        var emailOwner = await CandidateEmailUniqueness.FindExistingAsync(
            db, companyId, normalisedEmail, cancellationToken, excludingCandidateId: linked?.Id);

        return emailOwner is not null ? ApplyForInternalVacancyResult.EmailInUse() : null;
    }

    private StagedApplication StageRecords(
        Guid vacancyId,
        EmployeeApplicantProfile applicant,
        Candidate? candidate,
        Guid newCandidateId,
        Guid initialStageId,
        StagedCandidateDocumentUpload staged,
        DateTimeOffset now)
    {
        var candidateCreated = false;
        var refreshed = false;
        var reactivated = false;
        CandidateAuditSnapshot? before = null;

        if (candidate is null)
        {
            candidate = Candidate.CreateForEmployee(
                newCandidateId,
                applicant.CompanyId,
                applicant.EmployeeId,
                applicant.FirstName,
                applicant.LastName,
                applicant.WorkEmail,
                applicant.PhoneNumber,
                now);
            db.Candidates.Add(candidate);
            candidateCreated = true;
        }
        else
        {
            before = Snapshot(candidate);
            refreshed = candidate.SyncEmployeeIdentity(
                applicant.EmployeeId,
                applicant.FirstName,
                applicant.LastName,
                applicant.WorkEmail,
                applicant.PhoneNumber,
                now);

            if (!candidate.IsActive)
            {
                candidate.Reactivate(applicant.EmployeeId, now);
                reactivated = true;
            }

            if (refreshed || reactivated)
                candidate.IncrementVersion();
        }

        var cvDocument = CandidateDocument.Create(
            Guid.NewGuid(),
            applicant.CompanyId,
            candidate.Id,
            CandidateApplicationIntake.SubmittedCvTitle,
            staged.FileName,
            staged.FileSize,
            staged.ContentType,
            staged.StorageKey,
            applicant.EmployeeId,
            now,
            CandidateDocumentKind.Cv);
        db.CandidateDocuments.Add(cvDocument);

        staged.Intent.MarkConfirmed(now);

        var application = Application.Create(
            Guid.NewGuid(),
            applicant.CompanyId,
            vacancyId,
            candidate.Id,
            initialStageId,
            notes: null,
            now,
            ApplicationSource.Internal);
        application.AttachCv(cvDocument, now);
        db.Applications.Add(application);

        return new StagedApplication(candidate, application, cvDocument, candidateCreated, refreshed, reactivated, before);
    }

    private async Task PublishAuditAsync(StagedApplication created, Guid employeeId, CancellationToken cancellationToken)
    {
        var application = created.Application;
        var candidate = created.Candidate;

        await auditPublisher.PublishAsync(
            new InternalApplicationSubmittedAuditEvent(
                application.CompanyId,
                application.Id,
                application.VacancyId,
                candidate.Id,
                employeeId,
                created.CvDocument.Id,
                created.CandidateCreated,
                created.CandidateIdentityRefreshed,
                created.CandidateReactivated,
                application.CreatedAt),
            cancellationToken);

        await auditPublisher.PublishAsync(
            new ApplicationCvReferenceChangedAuditEvent(
                application.CompanyId,
                application.Id,
                application.VacancyId,
                candidate.Id,
                PreviousCvDocumentId: null,
                NewCvDocumentId: created.CvDocument.Id,
                employeeId,
                application.CreatedAt),
            cancellationToken);

        if (created.CandidateIdentityRefreshed && created.IdentityBefore is { } before)
        {
            await auditPublisher.PublishAsync(
                new CandidateUpdatedAuditEvent(
                    candidate.CompanyId, candidate.Id, before, Snapshot(candidate), application.CreatedAt),
                cancellationToken);
        }

        if (created.CandidateReactivated)
        {
            await auditPublisher.PublishAsync(
                new CandidateReactivatedAuditEvent(
                    candidate.CompanyId,
                    candidate.Id,
                    $"{candidate.FirstName} {candidate.LastName}".Trim(),
                    employeeId,
                    application.CreatedAt),
                cancellationToken);
        }
    }

    private static CandidateAuditSnapshot Snapshot(Candidate candidate) =>
        new(candidate.FirstName, candidate.LastName, candidate.Email, candidate.Phone);

    private async Task AcquireEmployeeLockAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
            return;

        var lockKey = $"recruitment:employee-candidate:{companyId:N}:{employeeId:N}";
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }

    private static bool IsUniqueViolationOn(DbUpdateException exception, string indexName) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        } postgres
        && string.Equals(postgres.ConstraintName, indexName, StringComparison.Ordinal);
}
