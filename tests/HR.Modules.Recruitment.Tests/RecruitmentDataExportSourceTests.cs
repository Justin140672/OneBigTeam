using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 1: the organisation data export's "applications" table carries the
/// submitted-CV reference as a trailing CvDocumentId column.
/// </summary>
public class RecruitmentDataExportSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task GetTablesAsync_Applications_Table_Ends_With_CvDocumentId_Column()
    {
        await using var db = BuildContext();

        var tables = await new RecruitmentDataExportSource(db).GetTablesAsync(Guid.NewGuid(), CancellationToken.None);

        var applications = Assert.Single(tables, t => t.Name == "applications");
        Assert.Equal("CvDocumentId", applications.Columns[^1]);
        Assert.Single(applications.Columns, c => c == "CvDocumentId");
    }

    [Fact]
    public async Task GetTablesAsync_Exports_Referenced_Cv_Id_And_Null_For_Applications_Without_A_Reference()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancyA = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var vacancyB = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var cv = CandidateDocument.Create(
            Guid.NewGuid(), companyId, candidate.Id, "CV", "cv.pdf", 2048, "application/pdf", "key", Guid.NewGuid(), Now, CandidateDocumentKind.Cv);
        var withCv = Application.Create(Guid.NewGuid(), companyId, vacancyA.Id, candidate.Id, stages.CvReview.Id, null, Now);
        withCv.AttachCv(cv, Now);
        var withoutCv = Application.Create(Guid.NewGuid(), companyId, vacancyB.Id, candidate.Id, stages.CvReview.Id, null, Now);
        db.Vacancies.AddRange(vacancyA, vacancyB);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.Add(cv);
        db.Applications.AddRange(withCv, withoutCv);
        await db.SaveChangesAsync();

        var tables = await new RecruitmentDataExportSource(db).GetTablesAsync(companyId, CancellationToken.None);

        var applications = Assert.Single(tables, t => t.Name == "applications");
        var idIndex = IndexOf(applications.Columns, "Id");
        var cvIndex = IndexOf(applications.Columns, "CvDocumentId");
        Assert.Equal(2, applications.Rows.Count);
        Assert.All(applications.Rows, row => Assert.Equal(applications.Columns.Count, row.Count));

        var rowsById = applications.Rows.ToDictionary(r => r[idIndex]!);
        Assert.Equal(cv.Id.ToString(), rowsById[withCv.Id.ToString()][cvIndex]);
        Assert.Null(rowsById[withoutCv.Id.ToString()][cvIndex]);
    }

    [Fact]
    public async Task GetTablesAsync_Does_Not_Export_Another_Companys_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, otherCompanyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), otherCompanyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var cv = CandidateDocument.Create(
            Guid.NewGuid(), otherCompanyId, candidate.Id, "CV", "cv.pdf", 2048, "application/pdf", "key", Guid.NewGuid(), Now, CandidateDocumentKind.Cv);
        var application = Application.Create(Guid.NewGuid(), otherCompanyId, vacancy.Id, candidate.Id, stages.CvReview.Id, null, Now);
        application.AttachCv(cv, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.Add(cv);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var tables = await new RecruitmentDataExportSource(db).GetTablesAsync(companyId, CancellationToken.None);

        Assert.Empty(Assert.Single(tables, t => t.Name == "applications").Rows);
    }

    // ----- Internal recruitment Ticket 6: Source + IsInternal columns -----

    [Fact]
    public async Task GetTablesAsync_Applications_Table_Has_Source_And_IsInternal_Before_Trailing_CvDocumentId()
    {
        await using var db = BuildContext();

        var tables = await new RecruitmentDataExportSource(db).GetTablesAsync(Guid.NewGuid(), CancellationToken.None);

        var applications = Assert.Single(tables, t => t.Name == "applications");
        Assert.Equal(
            ["Id", "VacancyId", "CandidateId", "CurrentStageId", "InterviewOutcome", "RejectionReason", "WithdrawnAt", "OfferApprovedAt", "AppliedAt", "Source", "IsInternal", "CvDocumentId"],
            applications.Columns);
    }

    [Fact]
    public async Task GetTablesAsync_Exports_Source_And_IsInternal_For_Internal_External_HiredExternal_And_Legacy_Rows()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        var (_, internalApp) = InternalApplicationTestData.AddInternal(db, companyId, vacancy.Id, stages.ApplicationReceived.Id, Guid.NewGuid(), Now);
        var (_, directApp)   = InternalApplicationTestData.AddExternal(db, companyId, vacancy.Id, stages.ApplicationReceived.Id, ApplicationSource.Direct, Now);
        var (_, legacyApp)   = InternalApplicationTestData.AddExternal(db, companyId, vacancy.Id, stages.CvReview.Id, null, Now, "Noah", "Patel");
        var (_, hiredApp)    = InternalApplicationTestData.AddHiredExternal(db, companyId, vacancy.Id, stages.Hired.Id, ApplicationSource.JobBoard, Guid.NewGuid(), Now);
        await db.SaveChangesAsync();

        var tables = await new RecruitmentDataExportSource(db).GetTablesAsync(companyId, CancellationToken.None);

        var applications = Assert.Single(tables, t => t.Name == "applications");
        var idIndex = IndexOf(applications.Columns, "Id");
        var sourceIndex = IndexOf(applications.Columns, "Source");
        var isInternalIndex = IndexOf(applications.Columns, "IsInternal");
        Assert.Equal(4, applications.Rows.Count);
        Assert.All(applications.Rows, row => Assert.Equal(applications.Columns.Count, row.Count));
        var rowsById = applications.Rows.ToDictionary(r => r[idIndex]!);

        Assert.Equal("Internal", rowsById[internalApp.Id.ToString()][sourceIndex]);
        Assert.Equal("true", rowsById[internalApp.Id.ToString()][isInternalIndex]);

        Assert.Equal("Direct", rowsById[directApp.Id.ToString()][sourceIndex]);
        Assert.Equal("false", rowsById[directApp.Id.ToString()][isInternalIndex]);

        Assert.Null(rowsById[legacyApp.Id.ToString()][sourceIndex]);
        Assert.Equal("false", rowsById[legacyApp.Id.ToString()][isInternalIndex]);

        Assert.Equal("JobBoard", rowsById[hiredApp.Id.ToString()][sourceIndex]);
        Assert.Equal("false", rowsById[hiredApp.Id.ToString()][isInternalIndex]);
    }

    private static int IndexOf(IReadOnlyList<string> columns, string name)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i] == name)
                return i;
        }

        throw new Xunit.Sdk.XunitException($"Column '{name}' not found in [{string.Join(", ", columns)}].");
    }
}
