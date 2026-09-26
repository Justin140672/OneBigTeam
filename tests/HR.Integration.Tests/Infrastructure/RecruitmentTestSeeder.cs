using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Shared seed helpers for Recruitment integration tests — vacancies, candidates, applications,
/// interviews and candidate documents. Uses the module's own domain factories and the real
/// <see cref="RecruitmentStageSeeder"/> default stage set so persisted state matches production.
/// </summary>
internal static class RecruitmentTestSeeder
{
    public sealed record SeededApplication(
        Guid VacancyId,
        Guid CandidateId,
        Guid ApplicationId,
        Guid CvReviewStageId,
        Guid HiredStageId,
        Guid RejectedStageId);

    public static async Task<Guid> SeedVacancyAsync(
        ApiWebApplicationFactory factory, Guid companyId, DateTimeOffset now, string advertTitle = "Backend Engineer")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), advertTitle, null, Guid.NewGuid(), now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return vacancy.Id;
    }

    public static async Task<Guid> SeedCandidateAsync(
        ApiWebApplicationFactory factory, Guid companyId, DateTimeOffset now,
        string firstName = "Emma", string lastName = "Clarke", string? email = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var candidate = Candidate.Create(
            Guid.NewGuid(), companyId, firstName, lastName,
            email ?? $"{firstName.ToLowerInvariant()}.{Guid.NewGuid():N}@example.com", null, null, now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate.Id;
    }

    /// <summary>
    /// Seeds the default recruitment stages, a vacancy, a candidate and an application sitting on the
    /// "CV Review" (non-terminal) stage.
    /// </summary>
    public static async Task<SeededApplication> SeedApplicationAsync(
        ApiWebApplicationFactory factory, Guid companyId, DateTimeOffset now,
        string candidateFirstName = "Emma", string candidateLastName = "Clarke",
        // Ticket 16 (P2): callers that will exercise HireCandidate against this seeded application
        // (e.g. the concurrency race tests) must pass a REAL position profile id — one seeded via
        // EmployeeReferenceDataSeeder — since HireCandidateHandler resolves Department/Location via
        // IPositionProfileReader.GetSummaryAsync and returns NotFound for a made-up id. Callers that
        // never hire from this application can leave this null and get the previous random-id
        // behaviour, unchanged.
        Guid? positionProfileId = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();

        // Stages carry a unique (company_id, display_order) constraint, so only seed the default
        // set once per company — callers frequently seed several applications for the same company.
        var existingStages = await db.RecruitmentStages
            .Where(s => s.CompanyId == companyId)
            .ToListAsync();

        List<RecruitmentStage> stages;
        if (existingStages.Count > 0)
        {
            stages = existingStages;
        }
        else
        {
            stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, now).ToList();
            db.RecruitmentStages.AddRange(stages);
        }

        var cvReviewStageId = stages.Single(s => s.Name == "CV Review").Id;
        var hiredStageId = stages.Single(s => s.Name == "Hired").Id;
        var rejectedStageId = stages.Single(s => s.Name == "Rejected").Id;

        var vacancy = Vacancy.Create(
            Guid.NewGuid(), companyId, positionProfileId ?? Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), now);
        var candidate = Candidate.Create(
            Guid.NewGuid(), companyId, candidateFirstName, candidateLastName,
            $"{candidateFirstName.ToLowerInvariant()}.{Guid.NewGuid():N}@example.com", null, null, now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, cvReviewStageId, null, now);

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        return new SeededApplication(vacancy.Id, candidate.Id, application.Id, cvReviewStageId, hiredStageId, rejectedStageId);
    }

    public static async Task<Guid> SeedInterviewAsync(
        ApiWebApplicationFactory factory, Guid companyId, Guid applicationId, DateTimeOffset now,
        DateTimeOffset? scheduledAt = null, InterviewOutcome? recordOutcome = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var interview = Interview.Create(
            Guid.NewGuid(), companyId, applicationId, Guid.NewGuid(), scheduledAt ?? now.AddDays(2), 45, "Room 1", now);
        if (recordOutcome is { } outcome && outcome != InterviewOutcome.Pending)
        {
            if (outcome == InterviewOutcome.Cancelled)
                interview.Cancel(now);
            else
                interview.RecordOutcome(outcome, "seeded", now);
        }
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();
        return interview.Id;
    }

    /// <summary>
    /// Seeds a candidate document row (no blob is written to storage). [P1] Documents start
    /// <see cref="CandidateDocumentScanStatus.Pending"/> exactly as a real upload does, so a download
    /// of one is refused (409) until it is scanned; pass <paramref name="scanStatus"/> =
    /// <see cref="CandidateDocumentScanStatus.Clean"/> for a test that needs a downloadable document.
    /// The status is reached through the real domain transitions (see <see cref="ApplyScanStatus"/>).
    /// </summary>
    public static async Task<Guid> SeedCandidateDocumentAsync(
        ApiWebApplicationFactory factory, Guid companyId, Guid candidateId, DateTimeOffset now,
        string title = "CV", string fileName = "cv.pdf", CandidateDocumentKind kind = CandidateDocumentKind.Other,
        CandidateDocumentScanStatus scanStatus = CandidateDocumentScanStatus.Pending)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var document = CandidateDocument.Create(
            Guid.NewGuid(), companyId, candidateId, title, fileName, 2048, "application/pdf",
            $"{companyId}/{candidateId}/{Guid.NewGuid():N}/{fileName}", Guid.NewGuid(), now, kind);
        ApplyScanStatus(document, scanStatus, DateTimeOffset.UtcNow);
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();
        return document.Id;
    }

    /// <summary>
    /// [P1] Moves an existing Pending candidate document to <paramref name="scanStatus"/> through the
    /// domain methods (as ScanCandidateDocumentJob would), without running a scanner.
    /// </summary>
    public static async Task SetCandidateDocumentScanStatusAsync(
        ApiWebApplicationFactory factory, Guid documentId, CandidateDocumentScanStatus scanStatus)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var document = await db.CandidateDocuments.SingleAsync(d => d.Id == documentId);
        ApplyScanStatus(document, scanStatus, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    /// <summary>Drives a Pending document to the requested scan status via the domain API. Scan
    /// transitions are stamped with <paramref name="at"/> — callers pass the real current time so a
    /// Scanning claim stays live (the recurring reconciliation sweep releases claims older than the
    /// 15-minute lease), independent of the document's own CreatedAt.</summary>
    public static void ApplyScanStatus(CandidateDocument document, CandidateDocumentScanStatus scanStatus, DateTimeOffset at)
    {
        switch (scanStatus)
        {
            case CandidateDocumentScanStatus.Pending:
                break;
            case CandidateDocumentScanStatus.Scanning:
                document.BeginScanAttempt(at);
                break;
            case CandidateDocumentScanStatus.Clean:
                document.BeginScanAttempt(at);
                document.MarkScanClean(at);
                break;
            case CandidateDocumentScanStatus.Infected:
                document.BeginScanAttempt(at);
                document.MarkScanInfected("Eicar-Test-Signature", at);
                break;
            case CandidateDocumentScanStatus.Failed:
                for (var attempt = 0; attempt < CandidateDocument.MaxScanAttempts; attempt++)
                {
                    document.BeginScanAttempt(at);
                    document.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.ScannerUnavailable, at, at);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scanStatus), scanStatus, null);
        }
    }

    /// <summary>
    /// Internal recruitment Ticket 1: records <paramref name="documentId"/> as the CV submitted with
    /// the application directly via the domain (no HTTP, no audit), bumping Version as a real save
    /// would. The document must be a Kind = Cv document of the application's candidate.
    /// </summary>
    public static async Task AttachApplicationCvAsync(
        ApiWebApplicationFactory factory, Guid applicationId, Guid documentId, DateTimeOffset now)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var application = await db.Applications.SingleAsync(a => a.Id == applicationId);
        var document = await db.CandidateDocuments.AsNoTracking().SingleAsync(d => d.Id == documentId);
        application.AttachCv(document, now);
        application.IncrementVersion();
        await db.SaveChangesAsync();
    }

    public static async Task MarkApplicationOnStageAsync(
        ApiWebApplicationFactory factory, Guid applicationId, Guid stageId, DateTimeOffset now)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var application = await db.Applications.SingleAsync(a => a.Id == applicationId);
        application.MoveToStage(stageId, now);
        await db.SaveChangesAsync();
    }

    public static async Task WithdrawApplicationAsync(
        ApiWebApplicationFactory factory, Guid applicationId, DateTimeOffset now)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var application = await db.Applications.SingleAsync(a => a.Id == applicationId);
        application.Withdraw(now);
        await db.SaveChangesAsync();
    }
}
