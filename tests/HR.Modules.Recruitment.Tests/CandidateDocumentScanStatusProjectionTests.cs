using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetApplication;
using HR.Modules.Recruitment.Features.ListCandidateDocuments;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// [P1] ListCandidateDocuments exposes ScanStatus/IsDownloadable per document, and GetApplication
/// exposes CvScanStatus (submitted CV) and CurrentCandidateCvScanStatus (newest CV).
/// </summary>
public class CandidateDocumentScanStatusProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static CandidateDocument NewDocument(
        Guid companyId, Guid candidateId, CandidateDocumentScanStatus status, DateTimeOffset createdAt,
        CandidateDocumentKind kind = CandidateDocumentKind.Other)
    {
        var document = CandidateDocument.Create(
            Guid.NewGuid(), companyId, candidateId, $"Doc {status}", $"{status}.pdf", 1024, "application/pdf",
            $"key/{Guid.NewGuid():N}", Guid.NewGuid(), createdAt, kind);

        switch (status)
        {
            case CandidateDocumentScanStatus.Pending:
                break;
            case CandidateDocumentScanStatus.Scanning:
                document.BeginScanAttempt(createdAt);
                break;
            case CandidateDocumentScanStatus.Clean:
                document.BeginScanAttempt(createdAt);
                document.MarkScanClean(createdAt);
                break;
            case CandidateDocumentScanStatus.Infected:
                document.BeginScanAttempt(createdAt);
                document.MarkScanInfected("Eicar-Test-Signature", createdAt);
                break;
            case CandidateDocumentScanStatus.Failed:
                for (var i = 0; i < CandidateDocument.MaxScanAttempts; i++)
                {
                    document.BeginScanAttempt(createdAt);
                    document.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.ScannerUnavailable, createdAt, createdAt);
                }
                break;
        }

        return document;
    }

    [Fact]
    public async Task ListCandidateDocuments_Projects_ScanStatus_And_Only_Clean_Is_Downloadable()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);

        var statuses = Enum.GetValues<CandidateDocumentScanStatus>();
        var documents = statuses
            .Select((s, i) => NewDocument(companyId, candidate.Id, s, Now.AddMinutes(i)))
            .ToList();
        db.CandidateDocuments.AddRange(documents);
        await db.SaveChangesAsync();

        var result = await new ListCandidateDocumentsHandler(db).HandleAsync(
            new ListCandidateDocumentsRequest { CompanyId = companyId, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(statuses.Length, result.Value!.Items.Count);
        foreach (var document in documents)
        {
            var item = Assert.Single(result.Value.Items, i => i.Id == document.Id);
            Assert.Equal(document.ScanStatus.ToString(), item.ScanStatus);
            Assert.Equal(document.ScanStatus == CandidateDocumentScanStatus.Clean, item.IsDownloadable);
        }
    }

    [Fact]
    public async Task ListCandidateDocuments_Reports_A_Fresh_Upload_As_Pending_And_Not_Downloadable()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.Add(NewDocument(companyId, candidate.Id, CandidateDocumentScanStatus.Pending, Now));
        await db.SaveChangesAsync();

        var result = await new ListCandidateDocumentsHandler(db).HandleAsync(
            new ListCandidateDocumentsRequest { CompanyId = companyId, CandidateId = candidate.Id },
            CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Pending", item.ScanStatus);
        Assert.False(item.IsDownloadable);
    }

    [Fact]
    public async Task GetApplication_Projects_Submitted_And_Current_Cv_Scan_Status_Independently()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var submittedCv = NewDocument(companyId, candidate.Id, CandidateDocumentScanStatus.Clean, Now, CandidateDocumentKind.Cv);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        application.AttachCv(submittedCv, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.CandidateDocuments.Add(submittedCv);
        // A newer CV, uploaded after the application, still infected/quarantined.
        db.CandidateDocuments.Add(NewDocument(companyId, candidate.Id, CandidateDocumentScanStatus.Infected, Now.AddDays(1), CandidateDocumentKind.Cv));
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(submittedCv.Id, result.Value!.CvDocumentId);
        Assert.Equal("Clean", result.Value.CvScanStatus);
        Assert.Equal("Infected", result.Value.CurrentCandidateCvScanStatus);
    }

    [Fact]
    public async Task GetApplication_Reports_Pending_For_Unscanned_Submitted_Cv()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var cv = NewDocument(companyId, candidate.Id, CandidateDocumentScanStatus.Pending, Now, CandidateDocumentKind.Cv);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        application.AttachCv(cv, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.CandidateDocuments.Add(cv);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.Equal("Pending", result.Value!.CvScanStatus);
        Assert.Equal("Pending", result.Value.CurrentCandidateCvScanStatus);
    }

    [Fact]
    public async Task GetApplication_Scan_Statuses_Are_Null_When_There_Is_No_Cv()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.Null(result.Value!.CvScanStatus);
        Assert.Null(result.Value.CurrentCandidateCvScanStatus);
    }
}
