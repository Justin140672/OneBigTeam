using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

public class ApplicationCvReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Application CreateApplication() =>
        Application.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now);

    [Fact]
    public void RecordCvReview_Trims_Notes_And_Stamps_Fields()
    {
        var application = CreateApplication();
        var reviewedBy = Guid.NewGuid();
        var later = Now.AddDays(1);

        application.RecordCvReview("  Strong Java background  ", reviewedBy, later);

        Assert.Equal("Strong Java background", application.CvReviewNotes);
        Assert.Equal(later, application.CvReviewedAt);
        Assert.Equal(reviewedBy, application.CvReviewedByUserId);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n ")]
    public void RecordCvReview_Normalises_Empty_Or_Whitespace_Notes_To_Null(string? notes)
    {
        var application = CreateApplication();
        var reviewedBy = Guid.NewGuid();

        application.RecordCvReview(notes, reviewedBy, Now.AddDays(1));

        Assert.Null(application.CvReviewNotes);
        // The review is still stamped even when notes are cleared.
        Assert.Equal(Now.AddDays(1), application.CvReviewedAt);
        Assert.Equal(reviewedBy, application.CvReviewedByUserId);
    }

    [Fact]
    public void RecordCvReview_Does_Not_Change_CurrentStageId()
    {
        var stageId = Guid.NewGuid();
        var application = Application.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), stageId, null, Now);

        application.RecordCvReview("notes", Guid.NewGuid(), Now.AddDays(1));

        Assert.Equal(stageId, application.CurrentStageId);
    }

    [Fact]
    public void RecordCvReview_Called_Again_Overwrites_Previous_Review()
    {
        var application = CreateApplication();
        application.RecordCvReview("first", Guid.NewGuid(), Now.AddDays(1));

        var secondReviewer = Guid.NewGuid();
        application.RecordCvReview("second", secondReviewer, Now.AddDays(2));

        Assert.Equal("second", application.CvReviewNotes);
        Assert.Equal(secondReviewer, application.CvReviewedByUserId);
        Assert.Equal(Now.AddDays(2), application.CvReviewedAt);
    }

    [Fact]
    public void RecordCvReview_Can_Clear_Previously_Set_Notes()
    {
        var application = CreateApplication();
        application.RecordCvReview("first", Guid.NewGuid(), Now.AddDays(1));

        application.RecordCvReview("   ", Guid.NewGuid(), Now.AddDays(2));

        Assert.Null(application.CvReviewNotes);
    }

    [Fact]
    public void Create_Leaves_CvReview_Fields_Null()
    {
        var application = CreateApplication();

        Assert.Null(application.CvReviewNotes);
        Assert.Null(application.CvReviewedAt);
        Assert.Null(application.CvReviewedByUserId);
    }
}
