using HR.Web.Models;

namespace HR.Web.Tests;

public class KanbanCardActionResolverTests
{
    private static KanbanCandidateModel Candidate(
        bool isInternal = false,
        string? offerStatus = null,
        bool pending = false,
        string? stageOutcome = null,
        bool hasNext = false,
        string? appointmentStatus = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", null, Guid.NewGuid(), "Stage", false,
            DateTimeOffset.UtcNow, null, null, "Vacancy",
            IsInternal: isInternal,
            OfferResponseStatus: offerStatus,
            CurrentStageHasPendingInterview: pending,
            PendingInterviewId: pending ? Guid.NewGuid() : null,
            CurrentStageInterviewOutcome: stageOutcome,
            HasNextInterviewStage: hasNext,
            NextInterviewStageId: hasNext ? Guid.NewGuid() : null,
            NextInterviewStageName: hasNext ? "Second Interview" : null,
            InternalAppointmentStatus: appointmentStatus);

    private static KanbanCardAction Interview(KanbanCandidateModel c) =>
        KanbanCardActionResolver.Resolve(c, isInterviewStage: true, isOfferStage: false, canAppointInternal: true);

    private static KanbanCardAction Offer(KanbanCandidateModel c, bool canAppoint = true) =>
        KanbanCardActionResolver.Resolve(c, isInterviewStage: false, isOfferStage: true, canAppointInternal: canAppoint);

    [Fact]
    public void No_Further_Interview_Stage_Prompt_Shows_When_Passed_And_Nothing_Ahead() =>
        Assert.True(KanbanCardActionResolver.ShowNoFurtherInterviewStage(Candidate(stageOutcome: "Passed"), isInterviewStage: true));

    [Fact]
    public void No_Further_Interview_Stage_Prompt_Hidden_When_Another_Stage_Is_Ahead() =>
        Assert.False(KanbanCardActionResolver.ShowNoFurtherInterviewStage(Candidate(stageOutcome: "Passed", hasNext: true), isInterviewStage: true));

    [Fact]
    public void No_Further_Interview_Stage_Prompt_Hidden_Outside_Interview_Stage() =>
        Assert.False(KanbanCardActionResolver.ShowNoFurtherInterviewStage(Candidate(stageOutcome: "Passed"), isInterviewStage: false));

    [Fact]
    public void Add_Interview_Stage_Link_Shows_Only_With_Stage_Management_Permission()
    {
        var candidate = Candidate(stageOutcome: "Passed");

        Assert.True(KanbanCardActionResolver.ShowAddInterviewStageLink(candidate, isInterviewStage: true, canManageStages: true));
        Assert.False(KanbanCardActionResolver.ShowAddInterviewStageLink(candidate, isInterviewStage: true, canManageStages: false));
    }

    [Fact]
    public void Interview_Stage_Without_Interview_Offers_Schedule() =>
        Assert.Equal(KanbanCardAction.ScheduleInterview, Interview(Candidate()));

    [Fact]
    public void Interview_Stage_With_Pending_Interview_Offers_Record_Outcome() =>
        Assert.Equal(KanbanCardAction.RecordOutcome, Interview(Candidate(pending: true, stageOutcome: "Passed")));

    [Fact]
    public void Passed_With_Another_Interview_Stage_Ahead_Offers_Move_To_Next_Stage() =>
        Assert.Equal(KanbanCardAction.MoveToNextInterviewStage, Interview(Candidate(stageOutcome: "Passed", hasNext: true)));

    [Fact]
    public void Passed_In_Final_Interview_Stage_Offers_Make_Offer() =>
        Assert.Equal(KanbanCardAction.MakeOffer, Interview(Candidate(stageOutcome: "Passed")));

    [Fact]
    public void Previous_Stage_Pass_Does_Not_Offer_Make_Offer_In_Later_Stage()
    {
        var card = Candidate(stageOutcome: null) with { InterviewOutcome = "Passed" };
        Assert.Equal(KanbanCardAction.ScheduleInterview, Interview(card));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("NoShow")]
    [InlineData("Cancelled")]
    public void Non_Passing_Latest_Outcome_Offers_Schedule(string outcome) =>
        Assert.Equal(KanbanCardAction.ScheduleInterview, Interview(Candidate(stageOutcome: outcome)));

    [Fact]
    public void Accepted_External_Offer_Offers_Hire() =>
        Assert.Equal(KanbanCardAction.Hire, Offer(Candidate(offerStatus: "Accepted")));

    [Fact]
    public void Accepted_Internal_Offer_Offers_Appoint_Not_Hire_Or_Review_Cv() =>
        Assert.Equal(KanbanCardAction.Appoint, Offer(Candidate(isInternal: true, offerStatus: "Accepted")));

    [Fact]
    public void Accepted_Internal_Offer_Without_Permission_Offers_Nothing_Not_Review_Cv() =>
        Assert.Equal(KanbanCardAction.None, Offer(Candidate(isInternal: true, offerStatus: "Accepted"), canAppoint: false));

    [Fact]
    public void Completed_Appointment_Offers_Nothing() =>
        Assert.Equal(KanbanCardAction.None, Offer(Candidate(isInternal: true, offerStatus: "Accepted", appointmentStatus: "Completed")));

    [Fact]
    public void Pending_Appointment_Still_Offers_Appoint() =>
        Assert.Equal(KanbanCardAction.Appoint, Offer(Candidate(isInternal: true, offerStatus: "Accepted", appointmentStatus: "Pending")));

    [Theory]
    [InlineData("Declined")]
    [InlineData("Withdrawn")]
    public void Declined_Or_Withdrawn_Offer_Does_Not_Fall_Back_To_Review_Cv(string status) =>
        Assert.Equal(KanbanCardAction.None, Offer(Candidate(offerStatus: status)));

    [Fact]
    public void Awaiting_Response_Offers_Record_Offer_Response() =>
        Assert.Equal(KanbanCardAction.RecordOfferResponse, Offer(Candidate(offerStatus: "AwaitingResponse")));

    [Fact]
    public void Offer_Stage_Without_Offer_Offers_Make_Offer() =>
        Assert.Equal(KanbanCardAction.MakeOffer, Offer(Candidate()));

    [Fact]
    public void Non_Interview_Non_Offer_Stage_Offers_Review_Cv() =>
        Assert.Equal(
            KanbanCardAction.ReviewCv,
            KanbanCardActionResolver.Resolve(Candidate(), isInterviewStage: false, isOfferStage: false, canAppointInternal: true));

    private static ApplicationListItemModel Row(
        bool pending = false, string? stageOutcome = null, bool hasNext = false, bool allPassed = true,
        string? offerStatus = null, bool isInternal = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "e@example.com", Guid.NewGuid(), "Passed", false,
            DateTimeOffset.UtcNow,
            OfferResponseStatus: offerStatus,
            IsInternal: isInternal,
            CurrentStageHasPendingInterview: pending,
            PendingInterviewId: pending ? Guid.NewGuid() : null,
            CurrentStageInterviewOutcome: stageOutcome,
            HasNextInterviewStage: hasNext,
            NextInterviewStageId: hasNext ? Guid.NewGuid() : null,
            AllRequiredInterviewStagesPassed: allPassed);

    [Fact]
    public void Applications_Tab_Second_Stage_With_Stale_Application_Wide_Pass_Offers_Schedule_Not_Offer() =>
        Assert.Equal(
            KanbanCardAction.ScheduleInterview,
            KanbanCardActionResolver.Resolve(Row(allPassed: false), isInterviewStage: true, isOfferStage: false, canAppointInternal: true));

    [Fact]
    public void Applications_Tab_Passed_With_Next_Stage_Offers_Progression() =>
        Assert.Equal(
            KanbanCardAction.MoveToNextInterviewStage,
            KanbanCardActionResolver.Resolve(Row(stageOutcome: "Passed", hasNext: true, allPassed: false), true, false, true));

    [Fact]
    public void Applications_Tab_Final_Pass_Offers_Make_Offer_Only_When_All_Required_Passed()
    {
        Assert.Equal(KanbanCardAction.MakeOffer,
            KanbanCardActionResolver.Resolve(Row(stageOutcome: "Passed"), true, false, true));
        Assert.Equal(KanbanCardAction.None,
            KanbanCardActionResolver.Resolve(Row(stageOutcome: "Passed", allPassed: false), true, false, true));
    }

    [Fact]
    public void Offer_Stage_Without_Eligibility_Does_Not_Offer_Make_Offer() =>
        Assert.Equal(KanbanCardAction.None, KanbanCardActionResolver.Resolve(Row(allPassed: false), false, true, true));

    [Fact]
    public void Applications_Tab_Accepted_Offer_Resolves_Hire_For_External_And_Appoint_For_Internal()
    {
        Assert.Equal(KanbanCardAction.Hire,
            KanbanCardActionResolver.Resolve(Row(offerStatus: "Accepted"), false, true, true));
        Assert.Equal(KanbanCardAction.Appoint,
            KanbanCardActionResolver.Resolve(Row(offerStatus: "Accepted", isInternal: true), false, true, true));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("NoShow")]
    [InlineData("Cancelled")]
    public void Applications_Tab_Non_Passed_Outcome_Allows_Retry_Schedule(string outcome) =>
        Assert.Equal(KanbanCardAction.ScheduleInterview,
            KanbanCardActionResolver.Resolve(Row(stageOutcome: outcome, allPassed: false), true, false, true));

    [Fact]
    public void Schedule_Available_From_Non_Interview_Stage_Only_When_Interview_Stage_Follows()
    {
        var withNext = Candidate(hasNext: true);
        var withoutNext = Candidate();

        Assert.True(KanbanCardActionResolver.CanScheduleInterview(
            withNext, KanbanCardActionResolver.Resolve(withNext, false, false, true)));
        Assert.False(KanbanCardActionResolver.CanScheduleInterview(
            withoutNext, KanbanCardActionResolver.Resolve(withoutNext, false, false, true)));
    }

    [Fact]
    public void Schedule_Available_In_Interview_Stage_When_Action_Is_Schedule() =>
        Assert.True(KanbanCardActionResolver.CanScheduleInterview(Candidate(), KanbanCardAction.ScheduleInterview));
}
