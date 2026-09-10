using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

public class CandidateDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static CandidateDocument Create(CandidateDocumentKind? kind = null) =>
        kind is null
            ? CandidateDocument.Create(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Resume", "cv.pdf", 1024, "application/pdf", "key", Guid.NewGuid(), Now)
            : CandidateDocument.Create(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Resume", "cv.pdf", 1024, "application/pdf", "key", Guid.NewGuid(), Now, kind.Value);

    [Fact]
    public void Create_Defaults_Kind_To_Other_When_Not_Specified()
    {
        var document = Create();

        Assert.Equal(CandidateDocumentKind.Other, document.Kind);
    }

    [Fact]
    public void Create_Respects_Explicit_Cv_Kind()
    {
        var document = Create(CandidateDocumentKind.Cv);

        Assert.Equal(CandidateDocumentKind.Cv, document.Kind);
    }

    [Fact]
    public void Create_Respects_Explicit_Other_Kind()
    {
        var document = Create(CandidateDocumentKind.Other);

        Assert.Equal(CandidateDocumentKind.Other, document.Kind);
    }

    [Fact]
    public void Create_Trims_Text_Fields()
    {
        var document = CandidateDocument.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  Resume  ", "  cv.pdf  ", 1024, "  application/pdf  ", "  key  ", Guid.NewGuid(), Now, CandidateDocumentKind.Cv);

        Assert.Equal("Resume", document.Title);
        Assert.Equal("cv.pdf", document.FileName);
        Assert.Equal("application/pdf", document.ContentType);
        Assert.Equal("key", document.StorageKey);
    }
}
