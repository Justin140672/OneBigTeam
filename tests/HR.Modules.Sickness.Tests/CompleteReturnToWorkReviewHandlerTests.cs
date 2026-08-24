using HR.Modules.Tasks.Contracts;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.CompleteReturnToWorkReview;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class CompleteReturnToWorkReviewHandlerTests
{
    private static readonly DateTime NowUtc = new(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(NowUtc, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid SicknessManagePermissionId = new("00000000-0000-0000-0001-000000000015");

    private static SicknessDbContext BuildDbContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static SicknessResourceAuthorizer BuildHrAuthorizer() =>
        new(new FakePermissionAuthorizationService(SicknessManagePermissionId), new FakeDirectReportsReader());

    private static SicknessResourceAuthorizer BuildManagerAuthorizer(params Guid[] reportIds) =>
        new(new FakePermissionAuthorizationService(), new FakeDirectReportsReader(reportIds));

    private static CompleteReturnToWorkReviewHandler BuildHandler(
        SicknessDbContext db,
        SicknessResourceAuthorizer? authorizer = null,
        FakeTaskCompleter? taskCompleter = null,
        FakeAuditEventPublisher? auditPublisher = null,
        DateTime? nowUtc = null) =>
        new(db,
            authorizer ?? BuildHrAuthorizer(),
            taskCompleter ?? new FakeTaskCompleter(),
            auditPublisher ?? new FakeAuditEventPublisher(),
            new FakeClock(nowUtc ?? NowUtc));

    private static async Task<(SicknessRecord Record, ReturnToWorkReview Review)> SeedClosedRecordWithReview(
        SicknessDbContext db, Guid? employeeId = null)
    {
        var empId = employeeId ?? EmployeeId;
        var categoryId = Guid.NewGuid();
        db.SicknessCategories.Add(SicknessCategory.Create(categoryId, CompanyId, "Cold", 1, Now));

        var record = SicknessRecord.Create(
            Guid.NewGuid(), CompanyId, empId, categoryId,
            new DateOnly(2026, 6, 1), SicknessDayPart.FullDay,
            new DateOnly(2026, 6, 5), SicknessDayPart.FullDay,
            totalDays: 5m, notes: null,
            evidenceStatus: SicknessEvidenceStatus.NotRequired, now: Now);
        db.SicknessRecords.Add(record);

        var review = ReturnToWorkReview.Create(Guid.NewGuid(), CompanyId, record.Id, empId, new DateOnly(2026, 6, 9), Now);
        db.ReturnToWorkReviews.Add(review);

        await db.SaveChangesAsync();
        return (record, review);
    }

    private static CompleteReturnToWorkReviewRequest BuildRequest(
        Guid reviewId,
        FitToReturnOutcome outcome = FitToReturnOutcome.Fit,
        bool adjustmentsRequired = false,
        string? adjustmentDetails = null,
        string? managerNotes = null) => new()
    {
        CompanyId = CompanyId,
        ReviewId = reviewId,
        Outcome = outcome,
        AdjustmentsRequired = adjustmentsRequired,
        AdjustmentDetails = adjustmentDetails,
        ManagerNotes = managerNotes
    };

    [Fact]
    public async Task HandleAsync_FitOutcome_CompletesReview_DoesNotReopenRecord_CompletesTask()
    {
        await using var db = BuildDbContext();
        var (record, review) = await SeedClosedRecordWithReview(db);
        var taskCompleter = new FakeTaskCompleter();
        var reviewedBy = Guid.NewGuid();

        var handler = BuildHandler(db, taskCompleter: taskCompleter);
        var result = await handler.HandleAsync(BuildRequest(review.Id), reviewedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);
        Assert.Equal("Fit", result.Value.Outcome);
        Assert.False(result.Value.AdjustmentsRequired);
        Assert.Null(result.Value.AdjustmentDetails);
        Assert.Equal(reviewedBy, result.Value.ReviewedBy);

        var storedRecord = await db.SicknessRecords.AsNoTracking().SingleAsync(r => r.Id == record.Id);
        Assert.Equal(SicknessStatus.Closed, storedRecord.Status);
        Assert.NotNull(storedRecord.EndDate);

        var call = Assert.Single(taskCompleter.Calls);
        Assert.Equal(review.CompanyId, call.CompanyId);
        Assert.Equal(review.Id, call.SourceEntityId);
        Assert.Equal(TaskSource.Sickness, call.Source);
        Assert.Equal(TaskActionType.Review, call.ActionType);
        Assert.Equal(reviewedBy, call.CompletedBy);
    }

    [Fact]
    public async Task HandleAsync_NotFitOutcome_ReopensSicknessRecord()
    {
        await using var db = BuildDbContext();
        var (record, review) = await SeedClosedRecordWithReview(db);
        var auditPublisher = new FakeAuditEventPublisher();

        var handler = BuildHandler(db, auditPublisher: auditPublisher);
        var result = await handler.HandleAsync(
            BuildRequest(review.Id, FitToReturnOutcome.NotFit), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("NotFit", result.Value!.Outcome);

        var storedRecord = await db.SicknessRecords.AsNoTracking().SingleAsync(r => r.Id == record.Id);
        Assert.Equal(SicknessStatus.Active, storedRecord.Status);
        Assert.Null(storedRecord.EndDate);
        Assert.Null(storedRecord.EndDayPart);
        Assert.Null(storedRecord.ReturnToWorkDate);
        Assert.Null(storedRecord.TotalDays);

        var reopenedEvent = Assert.Single(auditPublisher.PublishedEvents.OfType<SicknessRecordReopenedAuditEvent>());
        Assert.Equal(record.Id, reopenedEvent.SicknessRecordId);
        Assert.Equal(review.Id, reopenedEvent.ReviewId);
    }

    [Fact]
    public async Task HandleAsync_FitWithAdjustments_WithDetails_Succeeds()
    {
        await using var db = BuildDbContext();
        var (_, review) = await SeedClosedRecordWithReview(db);

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            BuildRequest(review.Id, FitToReturnOutcome.FitWithAdjustments, adjustmentsRequired: true,
                adjustmentDetails: "Phased return over two weeks."),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("FitWithAdjustments", result.Value!.Outcome);
        Assert.True(result.Value.AdjustmentsRequired);
        Assert.Equal("Phased return over two weeks.", result.Value.AdjustmentDetails);

        var storedRecord = await db.SicknessRecords.AsNoTracking().SingleAsync();
        Assert.Equal(SicknessStatus.Closed, storedRecord.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Review_Does_Not_Exist()
    {
        await using var db = BuildDbContext();
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(BuildRequest(Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unrelated_Manager()
    {
        await using var db = BuildDbContext();
        var (_, review) = await SeedClosedRecordWithReview(db);

        // Manager's reporting hierarchy does not include the review's employee.
        var handler = BuildHandler(db, authorizer: BuildManagerAuthorizer(Guid.NewGuid()));
        var result = await handler.HandleAsync(BuildRequest(review.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_For_Manager_In_Reporting_Hierarchy()
    {
        await using var db = BuildDbContext();
        var (_, review) = await SeedClosedRecordWithReview(db);

        var handler = BuildHandler(db, authorizer: BuildManagerAuthorizer(EmployeeId));
        var result = await handler.HandleAsync(BuildRequest(review.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_For_HrAdministrator_Regardless_Of_Hierarchy()
    {
        await using var db = BuildDbContext();
        var (_, review) = await SeedClosedRecordWithReview(db);

        var handler = BuildHandler(db, authorizer: BuildHrAuthorizer());
        var result = await handler.HandleAsync(BuildRequest(review.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_CalledTwice_OnlyMutatesAuditsAndCompletesTaskOnce()
    {
        await using var db = BuildDbContext();
        var (_, review) = await SeedClosedRecordWithReview(db);
        var taskCompleter = new FakeTaskCompleter();
        var auditPublisher = new FakeAuditEventPublisher();
        var reviewedBy = Guid.NewGuid();

        var handler = BuildHandler(db, taskCompleter: taskCompleter, auditPublisher: auditPublisher);

        var firstResult = await handler.HandleAsync(
            BuildRequest(review.Id, FitToReturnOutcome.FitWithAdjustments, adjustmentsRequired: true, adjustmentDetails: "Reduced hours."),
            reviewedBy, CancellationToken.None);

        // Second call simulates a retried request / re-dispatch from the Tasks module. Different
        // reviewer/outcome supplied deliberately to prove they are ignored.
        var secondResult = await handler.HandleAsync(
            BuildRequest(review.Id, FitToReturnOutcome.NotFit, adjustmentsRequired: false, adjustmentDetails: null),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsSuccess);

        Assert.Equal(firstResult.Value!.Outcome, secondResult.Value!.Outcome);
        Assert.Equal(firstResult.Value.AdjustmentsRequired, secondResult.Value.AdjustmentsRequired);
        Assert.Equal(firstResult.Value.AdjustmentDetails, secondResult.Value.AdjustmentDetails);
        Assert.Equal(firstResult.Value.ReviewedBy, secondResult.Value.ReviewedBy);
        Assert.Equal(reviewedBy, secondResult.Value.ReviewedBy);

        Assert.Single(taskCompleter.Calls);
        Assert.Single(auditPublisher.PublishedEvents.OfType<ReturnToWorkReviewCompletedAuditEvent>());
    }

    [Fact]
    public async Task HandleAsync_ReviewedBy_AlwaysReflectsServerResolvedCaller()
    {
        await using var db = BuildDbContext();
        var (_, review) = await SeedClosedRecordWithReview(db);
        var reviewedBy = Guid.NewGuid();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(BuildRequest(review.Id), reviewedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(reviewedBy, result.Value!.ReviewedBy);

        var stored = await db.ReturnToWorkReviews.AsNoTracking().SingleAsync(r => r.Id == review.Id);
        Assert.Equal(reviewedBy, stored.ReviewedBy);
    }

    // SICK-06: ActorEmployeeId on the completed event is the reviewer, correctly distinct from
    // EmployeeId (the subject being reviewed).
    [Fact]
    public async Task HandleAsync_CompletedAuditEvent_ActorEmployeeId_Is_Reviewer_Distinct_From_Subject()
    {
        await using var db = BuildDbContext();
        var (_, review) = await SeedClosedRecordWithReview(db);
        var auditPublisher = new FakeAuditEventPublisher();
        var reviewedBy = Guid.NewGuid();

        var handler = BuildHandler(db, auditPublisher: auditPublisher);
        var result = await handler.HandleAsync(BuildRequest(review.Id), reviewedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var completedEvent = Assert.Single(auditPublisher.PublishedEvents.OfType<ReturnToWorkReviewCompletedAuditEvent>());
        Assert.Equal(reviewedBy, ((HR.SharedKernel.IAuditEvent)completedEvent).ActorEmployeeId);
        Assert.Equal(EmployeeId, ((HR.SharedKernel.IAuditEvent)completedEvent).EmployeeId);
        Assert.NotEqual(EmployeeId, reviewedBy);
    }

    // SICK-06: AdjustmentDetails and Notes are free-text and must never be carried onto the
    // audit event — only boolean flags indicating whether they were populated.
    [Fact]
    public async Task HandleAsync_CompletedAuditEvent_Carries_Flags_Not_FreeText_For_AdjustmentDetails_And_Notes()
    {
        await using var db = BuildDbContext();
        var (_, review) = await SeedClosedRecordWithReview(db);
        var auditPublisher = new FakeAuditEventPublisher();
        const string sensitiveAdjustmentDetails = "SensitiveAdjustment-BackInjury-Detail";
        const string sensitiveManagerNotes = "SensitiveManagerNote-Depression-Detail";

        var handler = BuildHandler(db, auditPublisher: auditPublisher);
        var result = await handler.HandleAsync(
            BuildRequest(review.Id, FitToReturnOutcome.FitWithAdjustments, adjustmentsRequired: true,
                adjustmentDetails: sensitiveAdjustmentDetails, managerNotes: sensitiveManagerNotes),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var completedEvent = Assert.Single(auditPublisher.PublishedEvents.OfType<ReturnToWorkReviewCompletedAuditEvent>());
        Assert.True(completedEvent.HasAdjustmentDetails);
        Assert.True(completedEvent.HasNotes);

        var serialized = System.Text.Json.JsonSerializer.Serialize(completedEvent);
        Assert.DoesNotContain(sensitiveAdjustmentDetails, serialized);
        Assert.DoesNotContain(sensitiveManagerNotes, serialized);
    }

    [Fact]
    public async Task HandleAsync_CompletedAuditEvent_Flags_False_When_AdjustmentDetails_And_Notes_Not_Supplied()
    {
        await using var db = BuildDbContext();
        var (_, review) = await SeedClosedRecordWithReview(db);
        var auditPublisher = new FakeAuditEventPublisher();

        var handler = BuildHandler(db, auditPublisher: auditPublisher);
        var result = await handler.HandleAsync(
            BuildRequest(review.Id, FitToReturnOutcome.Fit, adjustmentsRequired: false,
                adjustmentDetails: null, managerNotes: null),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var completedEvent = Assert.Single(auditPublisher.PublishedEvents.OfType<ReturnToWorkReviewCompletedAuditEvent>());
        Assert.False(completedEvent.HasAdjustmentDetails);
        Assert.False(completedEvent.HasNotes);
    }

    [Fact]
    public async Task HandleAsync_CompletedAuditEvent_Flag_False_For_WhitespaceOnly_AdjustmentDetails()
    {
        // NotEmpty-style whitespace check: whitespace-only free text should not be treated as
        // "present" for the boolean flag.
        await using var db = BuildDbContext();
        var (_, review) = await SeedClosedRecordWithReview(db);
        var auditPublisher = new FakeAuditEventPublisher();

        var handler = BuildHandler(db, auditPublisher: auditPublisher);
        var result = await handler.HandleAsync(
            BuildRequest(review.Id, FitToReturnOutcome.FitWithAdjustments, adjustmentsRequired: true,
                adjustmentDetails: "   ", managerNotes: "   "),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var completedEvent = Assert.Single(auditPublisher.PublishedEvents.OfType<ReturnToWorkReviewCompletedAuditEvent>());
        Assert.False(completedEvent.HasAdjustmentDetails);
        Assert.False(completedEvent.HasNotes);
    }

    // SICK-06: the reopened event's actor is the reviewer who completed the review that caused
    // the reopen — never the affected employee.
    [Fact]
    public async Task HandleAsync_NotFitOutcome_ReopenedAuditEvent_ActorEmployeeId_Is_Reviewer_Not_Employee()
    {
        await using var db = BuildDbContext();
        var (_, review) = await SeedClosedRecordWithReview(db);
        var auditPublisher = new FakeAuditEventPublisher();
        var reviewedBy = Guid.NewGuid();

        var handler = BuildHandler(db, auditPublisher: auditPublisher);
        var result = await handler.HandleAsync(
            BuildRequest(review.Id, FitToReturnOutcome.NotFit), reviewedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reopenedEvent = Assert.Single(auditPublisher.PublishedEvents.OfType<SicknessRecordReopenedAuditEvent>());
        Assert.Equal(reviewedBy, ((HR.SharedKernel.IAuditEvent)reopenedEvent).ActorEmployeeId);
        Assert.NotEqual(EmployeeId, ((HR.SharedKernel.IAuditEvent)reopenedEvent).ActorEmployeeId);
    }
}
