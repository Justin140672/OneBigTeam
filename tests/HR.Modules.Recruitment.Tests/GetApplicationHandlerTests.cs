using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetApplication;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetApplicationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Application_With_Candidate_Details()
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

        Assert.True(result.IsSuccess);
        Assert.Equal(application.Id, result.Value!.Id);
        Assert.Equal("Emma", result.Value.CandidateFirstName);
        Assert.Equal("Clarke", result.Value.CandidateLastName);
        Assert.Equal("emma.clarke@example.com", result.Value.CandidateEmail);
        Assert.Equal(stages.ApplicationReceived.Id, result.Value.CurrentStageId);
        Assert.Equal("Application Received", result.Value.CurrentStageName);
        Assert.Null(result.Value.WithdrawnAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        await using var db = BuildContext();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = Guid.NewGuid(), VacancyId = Guid.NewGuid(), ApplicationId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Belongs_To_Different_Vacancy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var otherVacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        db.Vacancies.AddRange(vacancy, otherVacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = otherVacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_StageHistory_For_Freshly_Created_Application()
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

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.StageHistory);
    }

    [Fact]
    public async Task HandleAsync_Returns_StageHistory_Ordered_Oldest_First_After_Transitions()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.CvReview.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);

        var changedBy = Guid.NewGuid();
        var laterEntry = HR.Modules.Recruitment.Domain.ApplicationStageHistoryEntry.Create(
            Guid.NewGuid(), companyId, application.Id, stages.CvReview.Id, stages.Interview.Id,
            changedBy, "Scheduled first round.", Now.AddDays(2));
        var earlierEntry = HR.Modules.Recruitment.Domain.ApplicationStageHistoryEntry.Create(
            Guid.NewGuid(), companyId, application.Id, stages.ApplicationReceived.Id, stages.CvReview.Id,
            changedBy, "Passed CV screen.", Now.AddDays(1));
        db.ApplicationStageHistoryEntries.AddRange(laterEntry, earlierEntry);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [earlierEntry.Id, laterEntry.Id],
            result.Value!.StageHistory.Select(h => h.Id));
        Assert.Equal(stages.ApplicationReceived.Id, result.Value.StageHistory[0].PreviousStageId);
        Assert.Equal(stages.CvReview.Id, result.Value.StageHistory[0].NewStageId);
        Assert.Equal(changedBy, result.Value.StageHistory[0].ChangedByUserId);
        Assert.Equal("Passed CV screen.", result.Value.StageHistory[0].Notes);
    }

    [Fact]
    public async Task HandleAsync_Returns_Source_And_Recruiter_AgencyName_When_Source_Is_ExternalRecruiter()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now, ApplicationSource.ExternalRecruiter, recruiter.Id);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.ExternalRecruiters.Add(recruiter);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationSource.ExternalRecruiter, result.Value!.Source);
        Assert.Equal(recruiter.Id, result.Value.SourceExternalRecruiterId);
        Assert.Equal("Acme Recruiting", result.Value.SourceExternalRecruiterAgencyName);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_Source_Fields_When_Source_Was_Never_Set()
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

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Source);
        Assert.Null(result.Value.SourceExternalRecruiterId);
        Assert.Null(result.Value.SourceExternalRecruiterAgencyName);
    }

    [Fact]
    public async Task HandleAsync_Returns_WithdrawnAt_When_Application_Has_Been_Withdrawn()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        application.Withdraw(Now.AddDays(1));
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddDays(1), result.Value!.WithdrawnAt);
        Assert.Equal(stages.ApplicationReceived.Id, result.Value.CurrentStageId);
    }

    [Fact]
    public async Task HandleAsync_Returns_CvReviewNotes_And_Newest_Cv_As_Current_Candidate_Cv_Only_When_No_Cv_Submitted()
    {
        // Internal recruitment Ticket 1: the Cv* fields describe only the CV SUBMITTED with the
        // application. This application has no reference, so they are null even though the
        // candidate has CVs; the newest CV is surfaced separately as CurrentCandidateCv*.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        var reviewedBy = Guid.NewGuid();
        application.RecordCvReview("Strong CV", reviewedBy, Now.AddDays(1));

        var oldCv = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "CV v1", "cv-v1.pdf", 111, "application/pdf", "k1", Guid.NewGuid(), Now, CandidateDocumentKind.Cv);
        var newCv = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "CV v2", "cv-v2.pdf", 222, "application/pdf", "k2", Guid.NewGuid(), Now.AddMinutes(10), CandidateDocumentKind.Cv);
        var other = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Cover", "cover.pdf", 333, "application/pdf", "k3", Guid.NewGuid(), Now.AddMinutes(20));

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.CandidateDocuments.AddRange(oldCv, newCv, other);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Strong CV", result.Value!.CvReviewNotes);
        Assert.Equal(Now.AddDays(1), result.Value.CvReviewedAt);
        Assert.Equal(reviewedBy, result.Value.CvReviewedByUserId);

        Assert.Null(result.Value.CvDocumentId);
        Assert.Null(result.Value.CvFileName);
        Assert.Null(result.Value.CvContentType);
        Assert.Null(result.Value.CvFileSize);
        Assert.Null(result.Value.CvUploadedAt);

        Assert.Equal(newCv.Id, result.Value.CurrentCandidateCvDocumentId);
        Assert.Equal("cv-v2.pdf", result.Value.CurrentCandidateCvFileName);
        Assert.Equal("application/pdf", result.Value.CurrentCandidateCvContentType);
        Assert.Equal(222L, result.Value.CurrentCandidateCvFileSize);
        Assert.Equal(Now.AddMinutes(10), result.Value.CurrentCandidateCvUploadedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_Referenced_Older_Cv_Even_After_A_Newer_Cv_Is_Uploaded()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var submittedCv = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "CV v1", "cv-v1.pdf", 111, "application/pdf", "k1", Guid.NewGuid(), Now, CandidateDocumentKind.Cv);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        application.AttachCv(submittedCv, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.CandidateDocuments.Add(submittedCv);
        await db.SaveChangesAsync();

        // A newer CV is uploaded against the candidate after the application was submitted.
        var newerCv = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "CV v2", "cv-v2.pdf", 222, "application/pdf", "k2", Guid.NewGuid(), Now.AddDays(3), CandidateDocumentKind.Cv);
        db.CandidateDocuments.Add(newerCv);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(submittedCv.Id, result.Value!.CvDocumentId);
        Assert.Equal("cv-v1.pdf", result.Value.CvFileName);
        Assert.Equal("application/pdf", result.Value.CvContentType);
        Assert.Equal(111L, result.Value.CvFileSize);
        Assert.Equal(Now, result.Value.CvUploadedAt);

        Assert.Equal(newerCv.Id, result.Value.CurrentCandidateCvDocumentId);
        Assert.Equal("cv-v2.pdf", result.Value.CurrentCandidateCvFileName);
        Assert.Equal(Now.AddDays(3), result.Value.CurrentCandidateCvUploadedAt);
    }

    [Fact]
    public async Task HandleAsync_Submitted_And_Current_Cv_Are_The_Same_When_Latest_Cv_Was_Submitted()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var cv = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "CV", "cv.pdf", 111, "application/pdf", "k1", Guid.NewGuid(), Now, CandidateDocumentKind.Cv);
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

        Assert.True(result.IsSuccess);
        Assert.Equal(cv.Id, result.Value!.CvDocumentId);
        Assert.Equal(cv.Id, result.Value.CurrentCandidateCvDocumentId);
    }

    [Fact]
    public async Task HandleAsync_Two_Applications_Of_Same_Candidate_Return_Their_Own_Referenced_Cvs()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancyA = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var vacancyB = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var cvA = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Engineering CV", "cv-eng.pdf", 111, "application/pdf", "k1", Guid.NewGuid(), Now, CandidateDocumentKind.Cv);
        var cvB = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Design CV", "cv-design.pdf", 222, "application/pdf", "k2", Guid.NewGuid(), Now.AddMinutes(5), CandidateDocumentKind.Cv);
        var applicationA = Application.Create(Guid.NewGuid(), companyId, vacancyA.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        var applicationB = Application.Create(Guid.NewGuid(), companyId, vacancyB.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        applicationA.AttachCv(cvA, Now);
        applicationB.AttachCv(cvB, Now);
        db.Vacancies.AddRange(vacancyA, vacancyB);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.AddRange(cvA, cvB);
        db.Applications.AddRange(applicationA, applicationB);
        await db.SaveChangesAsync();

        var handler = new GetApplicationHandler(db);
        var resultA = await handler.HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancyA.Id, ApplicationId = applicationA.Id },
            CancellationToken.None);
        var resultB = await handler.HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancyB.Id, ApplicationId = applicationB.Id },
            CancellationToken.None);

        Assert.True(resultA.IsSuccess);
        Assert.True(resultB.IsSuccess);
        Assert.Equal(cvA.Id, resultA.Value!.CvDocumentId);
        Assert.Equal("cv-eng.pdf", resultA.Value.CvFileName);
        Assert.Equal(cvB.Id, resultB.Value!.CvDocumentId);
        Assert.Equal("cv-design.pdf", resultB.Value.CvFileName);

        // Both share the same "current" CV — the candidate's newest.
        Assert.Equal(cvB.Id, resultA.Value.CurrentCandidateCvDocumentId);
        Assert.Equal(cvB.Id, resultB.Value.CurrentCandidateCvDocumentId);
    }

    [Fact]
    public async Task HandleAsync_Loads_Historic_Application_With_No_Cv_Reference_And_No_Documents()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now.AddYears(-2));
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.CvDocumentId);
        Assert.Null(result.Value.CvFileName);
        Assert.Null(result.Value.CurrentCandidateCvDocumentId);
        Assert.Null(result.Value.CurrentCandidateCvFileName);
        Assert.Null(result.Value.CurrentCandidateCvContentType);
        Assert.Null(result.Value.CurrentCandidateCvFileSize);
        Assert.Null(result.Value.CurrentCandidateCvUploadedAt);
    }

    [Fact]
    public async Task HandleAsync_Current_Candidate_Cv_Ignores_Non_Cv_Documents_And_Other_Candidates()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var otherCandidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        var ownCv = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "CV", "own-cv.pdf", 111, "application/pdf", "k1", Guid.NewGuid(), Now, CandidateDocumentKind.Cv);
        var newerCoverLetter = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Cover", "cover.pdf", 222, "application/pdf", "k2", Guid.NewGuid(), Now.AddDays(1));
        var otherCandidatesNewerCv = CandidateDocument.Create(Guid.NewGuid(), companyId, otherCandidate.Id, "CV", "other-cv.pdf", 333, "application/pdf", "k3", Guid.NewGuid(), Now.AddDays(2), CandidateDocumentKind.Cv);
        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(candidate, otherCandidate);
        db.Applications.Add(application);
        db.CandidateDocuments.AddRange(ownCv, newerCoverLetter, otherCandidatesNewerCv);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ownCv.Id, result.Value!.CurrentCandidateCvDocumentId);
        Assert.Null(result.Value.CvDocumentId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Application_Version()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        application.IncrementVersion();
        application.IncrementVersion();
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Version);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_Cv_Summary_When_Candidate_Has_Only_Other_Documents()
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
        db.CandidateDocuments.Add(
            CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Cover", "cover.pdf", 333, "application/pdf", "k3", Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await new GetApplicationHandler(db).HandleAsync(
            new GetApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, ApplicationId = application.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.CvReviewNotes);
        Assert.Null(result.Value.CvDocumentId);
        Assert.Null(result.Value.CvFileName);
        Assert.Null(result.Value.CvContentType);
        Assert.Null(result.Value.CvFileSize);
        Assert.Null(result.Value.CvUploadedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_Cv_Summary_When_Candidate_Has_No_Documents()
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

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.CvDocumentId);
        Assert.Null(result.Value.CvUploadedAt);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
