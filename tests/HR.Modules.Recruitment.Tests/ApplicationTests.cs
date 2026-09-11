using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

public class ApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Application CreateApplication(Guid? initialStageId = null) =>
        Application.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), initialStageId ?? Guid.NewGuid(), null, Now);

    [Fact]
    public void Create_Sets_CurrentStageId_To_InitialStageId()
    {
        var stageId = Guid.NewGuid();
        var application = CreateApplication(stageId);

        Assert.Equal(stageId, application.CurrentStageId);
        Assert.Null(application.InterviewOutcome);
        Assert.Null(application.WithdrawnAt);
    }

    [Fact]
    public void SetInterviewOutcome_Sets_Outcome_And_UpdatedAt_Without_Changing_Stage()
    {
        var stageId = Guid.NewGuid();
        var application = CreateApplication(stageId);
        var later = Now.AddDays(1);

        application.SetInterviewOutcome(InterviewOutcome.Pending, later);

        Assert.Equal(InterviewOutcome.Pending, application.InterviewOutcome);
        Assert.Equal(stageId, application.CurrentStageId);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void MoveToStage_Updates_CurrentStageId_And_UpdatedAt()
    {
        var application = CreateApplication();
        var newStageId = Guid.NewGuid();
        var later = Now.AddDays(1);

        application.MoveToStage(newStageId, later);

        Assert.Equal(newStageId, application.CurrentStageId);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void RecordRejection_Sets_Stage_And_RejectionReason()
    {
        var application = CreateApplication();
        var rejectedStageId = Guid.NewGuid();
        var later = Now.AddDays(1);

        application.RecordRejection(rejectedStageId, "Not enough experience.", later);

        Assert.Equal(rejectedStageId, application.CurrentStageId);
        Assert.Equal("Not enough experience.", application.RejectionReason);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void RecordRejection_Trims_Whitespace_Only_Reason_To_Null()
    {
        var application = CreateApplication();

        application.RecordRejection(Guid.NewGuid(), "   ", Now.AddDays(1));

        Assert.Null(application.RejectionReason);
    }

    [Fact]
    public void RecordHire_Sets_Stage_And_UpdatedAt()
    {
        var application = CreateApplication();
        var hiredStageId = Guid.NewGuid();
        var later = Now.AddDays(1);

        application.RecordHire(hiredStageId, later);

        Assert.Equal(hiredStageId, application.CurrentStageId);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void Withdraw_Sets_WithdrawnAt_But_Does_Not_Change_CurrentStageId()
    {
        var stageId = Guid.NewGuid();
        var application = CreateApplication(stageId);
        var later = Now.AddDays(1);

        application.Withdraw(later);

        Assert.Equal(later, application.WithdrawnAt);
        Assert.Equal(stageId, application.CurrentStageId);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void Withdraw_When_InterviewOutcome_Pending_Sets_Outcome_To_Cancelled()
    {
        var application = CreateApplication();
        application.SetInterviewOutcome(InterviewOutcome.Pending, Now);

        application.Withdraw(Now.AddDays(1));

        Assert.Equal(InterviewOutcome.Cancelled, application.InterviewOutcome);
    }

    [Fact]
    public void Withdraw_When_InterviewOutcome_Already_Resolved_Leaves_It_Untouched()
    {
        // Withdraw() only overwrites a Pending outcome — an already-resolved historical outcome
        // (e.g. Passed) must not be silently changed. This covers the negated branch of that check.
        var application = CreateApplication();
        application.SetInterviewOutcome(InterviewOutcome.Passed, Now);

        application.Withdraw(Now.AddDays(1));

        Assert.Equal(InterviewOutcome.Passed, application.InterviewOutcome);
    }

    [Fact]
    public void Withdraw_When_No_InterviewOutcome_Set_Leaves_It_Null()
    {
        var application = CreateApplication();

        application.Withdraw(Now.AddDays(1));

        Assert.Null(application.InterviewOutcome);
    }

    [Fact]
    public void Create_With_Source_ExternalRecruiter_Sets_SourceExternalRecruiterId()
    {
        var recruiterId = Guid.NewGuid();

        var application = Application.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now,
            ApplicationSource.ExternalRecruiter, recruiterId);

        Assert.Equal(ApplicationSource.ExternalRecruiter, application.Source);
        Assert.Equal(recruiterId, application.SourceExternalRecruiterId);
    }

    [Fact]
    public void Create_Forces_SourceExternalRecruiterId_Null_When_Source_Is_Not_ExternalRecruiter()
    {
        var suppliedRecruiterId = Guid.NewGuid();

        var application = Application.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Now,
            ApplicationSource.Direct, suppliedRecruiterId);

        Assert.Equal(ApplicationSource.Direct, application.Source);
        Assert.Null(application.SourceExternalRecruiterId);
    }

    [Fact]
    public void Create_Without_Source_Leaves_Source_And_RecruiterId_Null()
    {
        var application = CreateApplication();

        Assert.Null(application.Source);
        Assert.Null(application.SourceExternalRecruiterId);
    }

    [Fact]
    public void SetSource_ExternalRecruiter_Sets_SourceExternalRecruiterId_And_UpdatedAt()
    {
        var application = CreateApplication();
        var recruiterId = Guid.NewGuid();
        var later = Now.AddDays(1);

        application.SetSource(ApplicationSource.ExternalRecruiter, recruiterId, later);

        Assert.Equal(ApplicationSource.ExternalRecruiter, application.Source);
        Assert.Equal(recruiterId, application.SourceExternalRecruiterId);
        Assert.Equal(later, application.UpdatedAt);
    }

    [Fact]
    public void SetSource_Forces_SourceExternalRecruiterId_Null_When_Source_Not_ExternalRecruiter_Even_If_Id_Supplied()
    {
        var application = CreateApplication();
        application.SetSource(ApplicationSource.ExternalRecruiter, Guid.NewGuid(), Now);
        var suppliedRecruiterId = Guid.NewGuid();

        application.SetSource(ApplicationSource.Direct, suppliedRecruiterId, Now.AddDays(1));

        Assert.Equal(ApplicationSource.Direct, application.Source);
        Assert.Null(application.SourceExternalRecruiterId);
    }

    [Fact]
    public void SetSource_Null_Clears_Source_And_RecruiterId()
    {
        var application = CreateApplication();
        application.SetSource(ApplicationSource.ExternalRecruiter, Guid.NewGuid(), Now);

        application.SetSource(null, null, Now.AddDays(1));

        Assert.Null(application.Source);
        Assert.Null(application.SourceExternalRecruiterId);
    }

    // SET-05: offer approval.

    [Fact]
    public void ApproveOffer_Sets_OfferApprovedAt_And_OfferApprovedByUserId()
    {
        var application = CreateApplication();
        var approvedBy = Guid.NewGuid();
        var later = Now.AddDays(1);

        application.ApproveOffer(approvedBy, later);

        Assert.Equal(later, application.OfferApprovedAt);
        Assert.Equal(approvedBy, application.OfferApprovedByUserId);
    }

    [Fact]
    public void Create_Defaults_OfferApprovedAt_And_OfferApprovedByUserId_To_Null()
    {
        var application = CreateApplication();

        Assert.Null(application.OfferApprovedAt);
        Assert.Null(application.OfferApprovedByUserId);
    }

    [Fact]
    public void ApproveOffer_Can_Be_Called_Again_And_Overwrites_Previous_Approval()
    {
        var application = CreateApplication();
        var firstApprover = Guid.NewGuid();
        application.ApproveOffer(firstApprover, Now.AddDays(1));

        var secondApprover = Guid.NewGuid();
        application.ApproveOffer(secondApprover, Now.AddDays(2));

        Assert.Equal(secondApprover, application.OfferApprovedByUserId);
        Assert.Equal(Now.AddDays(2), application.OfferApprovedAt);
    }

    // Ticket 2: offer terms + response.

    [Fact]
    public void Create_Defaults_All_Offer_Term_Fields_To_Null()
    {
        var application = CreateApplication();

        Assert.Null(application.OfferedSalary);
        Assert.Null(application.OfferedSalaryFrequency);
        Assert.Null(application.OfferedStartDate);
        Assert.Null(application.OfferDate);
        Assert.Null(application.OfferNotes);
        Assert.Null(application.OfferResponseStatus);
        Assert.Null(application.OfferMadeAt);
        Assert.Null(application.OfferRespondedAt);
    }

    [Fact]
    public void RecordOfferTerms_Sets_All_Fields_And_Puts_Offer_Into_AwaitingResponse()
    {
        var stageId = Guid.NewGuid();
        var application = CreateApplication(stageId);
        var later = Now.AddDays(1);
        var startDate = new DateOnly(2026, 3, 1);
        var offerDate = new DateOnly(2026, 1, 15);

        application.RecordOfferTerms(55000m, OfferSalaryFrequency.Annual, startDate, offerDate, "Standard package.", later);

        Assert.Equal(55000m, application.OfferedSalary);
        Assert.Equal(OfferSalaryFrequency.Annual, application.OfferedSalaryFrequency);
        Assert.Equal(startDate, application.OfferedStartDate);
        Assert.Equal(offerDate, application.OfferDate);
        Assert.Equal("Standard package.", application.OfferNotes);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, application.OfferResponseStatus);
        Assert.Equal(later, application.OfferMadeAt);
        Assert.Null(application.OfferRespondedAt);
        Assert.Equal(later, application.UpdatedAt);
        // Does not touch the pipeline stage — the caller owns the stage move.
        Assert.Equal(stageId, application.CurrentStageId);
    }

    [Fact]
    public void RecordOfferTerms_Trims_Notes_And_Maps_Whitespace_Only_To_Null()
    {
        var application = CreateApplication();

        application.RecordOfferTerms(null, null, null, new DateOnly(2026, 1, 15), "   Negotiated up.   ", Now);
        Assert.Equal("Negotiated up.", application.OfferNotes);

        application.RecordOfferTerms(null, null, null, new DateOnly(2026, 1, 15), "   ", Now);
        Assert.Null(application.OfferNotes);
    }

    [Fact]
    public void RecordOfferTerms_Accepts_Null_Salary_Frequency_And_Start_Date()
    {
        var application = CreateApplication();

        application.RecordOfferTerms(null, null, null, new DateOnly(2026, 1, 15), null, Now);

        Assert.Null(application.OfferedSalary);
        Assert.Null(application.OfferedSalaryFrequency);
        Assert.Null(application.OfferedStartDate);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, application.OfferResponseStatus);
    }

    [Fact]
    public void RecordOfferTerms_Called_Again_Re_Records_And_Clears_Prior_Response_Timestamp()
    {
        var application = CreateApplication();
        application.RecordOfferTerms(40000m, OfferSalaryFrequency.Annual, null, new DateOnly(2026, 1, 1), null, Now);
        application.RespondToOffer(OfferResponseStatus.Declined, Now.AddDays(1));

        application.RecordOfferTerms(45000m, OfferSalaryFrequency.Annual, null, new DateOnly(2026, 2, 1), null, Now.AddDays(2));

        Assert.Equal(45000m, application.OfferedSalary);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, application.OfferResponseStatus);
        Assert.Null(application.OfferRespondedAt);
        Assert.Equal(Now.AddDays(2), application.OfferMadeAt);
    }

    [Theory]
    [InlineData((int)OfferResponseStatus.Accepted)]
    [InlineData((int)OfferResponseStatus.Declined)]
    [InlineData((int)OfferResponseStatus.Withdrawn)]
    public void RespondToOffer_Sets_Status_And_OfferRespondedAt(int responseRaw)
    {
        var response = (OfferResponseStatus)responseRaw;
        var stageId = Guid.NewGuid();
        var application = CreateApplication(stageId);
        application.RecordOfferTerms(50000m, OfferSalaryFrequency.Annual, null, new DateOnly(2026, 1, 1), null, Now);
        var later = Now.AddDays(3);

        application.RespondToOffer(response, later);

        Assert.Equal(response, application.OfferResponseStatus);
        Assert.Equal(later, application.OfferRespondedAt);
        Assert.Equal(later, application.UpdatedAt);
        // Response never moves the pipeline stage.
        Assert.Equal(stageId, application.CurrentStageId);
    }
}
