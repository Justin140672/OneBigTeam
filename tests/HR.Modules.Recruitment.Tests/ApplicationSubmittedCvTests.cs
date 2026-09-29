using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 1: domain coverage for the submitted-CV reference on
/// <see cref="Application"/> — <see cref="Application.AttachCv"/>, <see cref="Application.RemoveCv"/>,
/// <see cref="Application.DescribeCvDocumentViolation"/> and the purge-time clearing in
/// <see cref="Application.RedactPersonalData"/>.
/// </summary>
public class ApplicationSubmittedCvTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid CandidateId = Guid.NewGuid();

    private static Application CreateApplication() =>
        Application.Create(Guid.NewGuid(), CompanyId, Guid.NewGuid(), CandidateId, Guid.NewGuid(), null, Now);

    private static CandidateDocument CreateDocument(
        Guid? companyId = null,
        Guid? candidateId = null,
        CandidateDocumentKind kind = CandidateDocumentKind.Cv,
        DateTimeOffset? createdAt = null) =>
        CandidateDocument.Create(
            Guid.NewGuid(), companyId ?? CompanyId, candidateId ?? CandidateId, "CV", "cv.pdf", 1024,
            "application/pdf", "storage/key/cv.pdf", Guid.NewGuid(), createdAt ?? Now, kind);


    [Fact]
    public void Create_Leaves_CvDocumentId_Null()
    {
        var application = CreateApplication();

        Assert.Null(application.CvDocumentId);
    }


    [Fact]
    public void AttachCv_Records_Document_Id_And_Updates_UpdatedAt()
    {
        var application = CreateApplication();
        var cv = CreateDocument();
        var later = Now.AddHours(1);

        var changed = application.AttachCv(cv, later);

        Assert.True(changed);
        Assert.Equal(cv.Id, application.CvDocumentId);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void AttachCv_Does_Not_Bump_Version_Itself()
    {
        var application = CreateApplication();

        application.AttachCv(CreateDocument(), Now.AddHours(1));

        Assert.Equal(1, application.Version);
    }

    [Fact]
    public void AttachCv_Returns_False_And_Changes_Nothing_When_Same_Document_Already_Referenced()
    {
        var application = CreateApplication();
        var cv = CreateDocument();
        var firstAttach = Now.AddHours(1);
        application.AttachCv(cv, firstAttach);

        var changed = application.AttachCv(cv, Now.AddHours(2));

        Assert.False(changed);
        Assert.Equal(cv.Id, application.CvDocumentId);
        Assert.Equal(firstAttach, application.UpdatedAt);
    }

    [Fact]
    public void AttachCv_Replaces_A_Previously_Referenced_Document()
    {
        var application = CreateApplication();
        var originalCv = CreateDocument();
        var replacementCv = CreateDocument(createdAt: Now.AddDays(1));
        application.AttachCv(originalCv, Now.AddHours(1));
        var later = Now.AddDays(2);

        var changed = application.AttachCv(replacementCv, later);

        Assert.True(changed);
        Assert.Equal(replacementCv.Id, application.CvDocumentId);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void AttachCv_Throws_For_Document_From_Another_Company_And_Leaves_State_Unchanged()
    {
        var application = CreateApplication();
        var foreignCv = CreateDocument(companyId: Guid.NewGuid());

        var ex = Assert.Throws<InvalidOperationException>(() => application.AttachCv(foreignCv, Now.AddHours(1)));

        Assert.Equal(Application.CvDocumentNotFoundMessage, ex.Message);
        Assert.Null(application.CvDocumentId);
        Assert.Equal(Now, application.UpdatedAt);
    }

    [Fact]
    public void AttachCv_Throws_For_Document_Of_Another_Candidate_And_Leaves_State_Unchanged()
    {
        var application = CreateApplication();
        var otherCandidatesCv = CreateDocument(candidateId: Guid.NewGuid());

        var ex = Assert.Throws<InvalidOperationException>(() => application.AttachCv(otherCandidatesCv, Now.AddHours(1)));

        Assert.Contains("different candidate", ex.Message);
        Assert.Null(application.CvDocumentId);
        Assert.Equal(Now, application.UpdatedAt);
    }

    [Fact]
    public void AttachCv_Throws_For_Non_Cv_Document_And_Leaves_State_Unchanged()
    {
        var application = CreateApplication();
        var coverLetter = CreateDocument(kind: CandidateDocumentKind.Other);

        var ex = Assert.Throws<InvalidOperationException>(() => application.AttachCv(coverLetter, Now.AddHours(1)));

        Assert.Contains("not a CV", ex.Message);
        Assert.Null(application.CvDocumentId);
        Assert.Equal(Now, application.UpdatedAt);
    }

    [Fact]
    public void AttachCv_Invalid_Replacement_Keeps_The_Existing_Reference()
    {
        var application = CreateApplication();
        var validCv = CreateDocument();
        application.AttachCv(validCv, Now.AddHours(1));

        Assert.Throws<InvalidOperationException>(
            () => application.AttachCv(CreateDocument(kind: CandidateDocumentKind.Other), Now.AddHours(2)));

        Assert.Equal(validCv.Id, application.CvDocumentId);
        Assert.Equal(Now.AddHours(1), application.UpdatedAt);
    }


    [Fact]
    public void RemoveCv_Clears_Reference_And_Updates_UpdatedAt()
    {
        var application = CreateApplication();
        application.AttachCv(CreateDocument(), Now.AddHours(1));
        var later = Now.AddHours(2);

        var changed = application.RemoveCv(later);

        Assert.True(changed);
        Assert.Null(application.CvDocumentId);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void RemoveCv_Returns_False_And_Changes_Nothing_When_No_Reference()
    {
        var application = CreateApplication();

        var changed = application.RemoveCv(Now.AddHours(1));

        Assert.False(changed);
        Assert.Null(application.CvDocumentId);
        Assert.Equal(Now, application.UpdatedAt);
    }

    [Fact]
    public void RemoveCv_Called_Twice_Is_A_NoOp_The_Second_Time()
    {
        var application = CreateApplication();
        application.AttachCv(CreateDocument(), Now.AddHours(1));
        var removedAt = Now.AddHours(2);

        Assert.True(application.RemoveCv(removedAt));
        Assert.False(application.RemoveCv(Now.AddHours(3)));

        Assert.Null(application.CvDocumentId);
        Assert.Equal(removedAt, application.UpdatedAt);
    }


    [Fact]
    public void RedactPersonalData_Clears_CvDocumentId()
    {
        var application = CreateApplication();
        application.AttachCv(CreateDocument(), Now.AddHours(1));

        application.RedactPersonalData(Now.AddDays(1));

        Assert.Null(application.CvDocumentId);
        Assert.Equal(Now.AddDays(1), application.UpdatedAt);
    }

    [Fact]
    public void RedactPersonalData_Succeeds_When_No_Cv_Was_Referenced()
    {
        var application = CreateApplication();

        application.RedactPersonalData(Now.AddDays(1));

        Assert.Null(application.CvDocumentId);
    }


    [Fact]
    public void DescribeCvDocumentViolation_Returns_Null_For_Cv_Of_Same_Company_And_Candidate()
    {
        Assert.Null(Application.DescribeCvDocumentViolation(CreateDocument(), CompanyId, CandidateId));
    }

    [Fact]
    public void DescribeCvDocumentViolation_Reports_Other_Company_Document_As_Not_Found()
    {
        // Deliberately indistinguishable from a missing document so another tenant's document
        // existence is never revealed.
        var message = Application.DescribeCvDocumentViolation(
            CreateDocument(companyId: Guid.NewGuid()), CompanyId, CandidateId);

        Assert.Equal(Application.CvDocumentNotFoundMessage, message);
    }

    [Fact]
    public void DescribeCvDocumentViolation_Reports_Other_Company_Before_Other_Candidate_Or_Kind()
    {
        var message = Application.DescribeCvDocumentViolation(
            CreateDocument(companyId: Guid.NewGuid(), candidateId: Guid.NewGuid(), kind: CandidateDocumentKind.Other),
            CompanyId, CandidateId);

        Assert.Equal(Application.CvDocumentNotFoundMessage, message);
    }

    [Fact]
    public void DescribeCvDocumentViolation_Reports_Different_Candidate()
    {
        var message = Application.DescribeCvDocumentViolation(
            CreateDocument(candidateId: Guid.NewGuid()), CompanyId, CandidateId);

        Assert.Equal("The selected CV document belongs to a different candidate.", message);
    }

    [Fact]
    public void DescribeCvDocumentViolation_Reports_Different_Candidate_Before_Kind()
    {
        var message = Application.DescribeCvDocumentViolation(
            CreateDocument(candidateId: Guid.NewGuid(), kind: CandidateDocumentKind.Other), CompanyId, CandidateId);

        Assert.Equal("The selected CV document belongs to a different candidate.", message);
    }

    [Fact]
    public void DescribeCvDocumentViolation_Reports_Non_Cv_Document()
    {
        var message = Application.DescribeCvDocumentViolation(
            CreateDocument(kind: CandidateDocumentKind.Other), CompanyId, CandidateId);

        Assert.NotNull(message);
        Assert.StartsWith("The selected document is not a CV.", message);
    }
}
