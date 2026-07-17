using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class DetectDocumentsDueForReviewJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today     = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static DetectDocumentsDueForReviewJob BuildJob(
        DocumentsDbContext db,
        FakeLogger<DetectDocumentsDueForReviewJob> logger,
        FakeTaskCreator? taskCreator = null,
        FakeOpenTaskBySourceEntityReader? openTaskReader = null,
        FakeEmployeeNameReader? employeeNameReader = null,
        FakeNotificationWriter? notificationWriter = null) =>
        new(db,
            new FakeClock(FixedUtcNow),
            taskCreator ?? new FakeTaskCreator(),
            openTaskReader ?? new FakeOpenTaskBySourceEntityReader(),
            employeeNameReader ?? new FakeEmployeeNameReader(),
            notificationWriter ?? new FakeNotificationWriter(),
            logger);

    private static async Task<CompanyDocumentCategory> SeedCategoryAsync(DocumentsDbContext db, Guid companyId)
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", Now);
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private static SharedCompanyDocument CreateDoc(
        Guid companyId, Guid categoryId, DateOnly? reviewDate,
        SharedCompanyDocumentStatus status = SharedCompanyDocumentStatus.Published,
        Guid? reviewOwnerEmployeeId = null)
    {
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Employee Handbook", null, categoryId, $"key/{Guid.NewGuid():N}.pdf",
            "handbook.pdf", 100, "application/pdf", null, reviewDate, SharedCompanyDocumentReviewFrequency.None,
            null, reviewOwnerEmployeeId, false, null, null, Guid.NewGuid(), Now);

        if (status is SharedCompanyDocumentStatus.Published or SharedCompanyDocumentStatus.Archived or SharedCompanyDocumentStatus.Expired)
            doc.Publish(Guid.NewGuid(), Now);

        if (status == SharedCompanyDocumentStatus.Archived)
            doc.Archive(Guid.NewGuid(), "Superseded", Now);

        if (status == SharedCompanyDocumentStatus.Expired)
            doc.MarkExpired(Guid.NewGuid(), Now);

        return doc;
    }

    private static int LoggedDueCount(FakeLogger<DetectDocumentsDueForReviewJob> logger)
    {
        Assert.True(logger.Messages.Count >= 1, "Expected at least a 'found' log message.");
        var message = logger.Messages[0];
        var match = System.Text.RegularExpressions.Regex.Match(message, @"found (\d+) shared company document");
        Assert.True(match.Success, $"Expected message to contain a due-count, got: {message}");
        return int.Parse(match.Groups[1].Value);
    }

    private static int LoggedCreatedCount(FakeLogger<DetectDocumentsDueForReviewJob> logger)
    {
        Assert.True(logger.Messages.Count >= 2, "Expected a 'created' log message following the 'found' message.");
        var message = logger.Messages[1];
        var match = System.Text.RegularExpressions.Regex.Match(message, @"created (\d+) review task");
        Assert.True(match.Success, $"Expected message to contain a created-count, got: {message}");
        return int.Parse(match.Groups[1].Value);
    }

    [Fact]
    public async Task ExecuteAsync_Counts_Document_With_ReviewDate_In_The_Past()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        db.SharedCompanyDocuments.Add(CreateDoc(companyId, category.Id, Today.AddDays(-5)));
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        await BuildJob(db, logger).ExecuteAsync();

        Assert.Equal(1, LoggedDueCount(logger));
        Assert.Equal(0, LoggedCreatedCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Counts_Document_With_ReviewDate_Equal_To_Today()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        db.SharedCompanyDocuments.Add(CreateDoc(companyId, category.Id, Today));
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        await BuildJob(db, logger).ExecuteAsync();

        Assert.Equal(1, LoggedDueCount(logger));
        Assert.Equal(0, LoggedCreatedCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Count_Document_With_ReviewDate_In_The_Future()
    {
        // Represents a review that has already been completed by moving ReviewDate forward.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        db.SharedCompanyDocuments.Add(CreateDoc(companyId, category.Id, Today.AddDays(5)));
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        await BuildJob(db, logger).ExecuteAsync();

        Assert.Equal(0, LoggedDueCount(logger));
        Assert.Equal(0, LoggedCreatedCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Count_Document_With_Null_ReviewDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        db.SharedCompanyDocuments.Add(CreateDoc(companyId, category.Id, reviewDate: null));
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        await BuildJob(db, logger).ExecuteAsync();

        Assert.Equal(0, LoggedDueCount(logger));
        Assert.Equal(0, LoggedCreatedCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Count_Archived_Document_Even_When_Overdue()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        db.SharedCompanyDocuments.Add(
            CreateDoc(companyId, category.Id, Today.AddDays(-10), SharedCompanyDocumentStatus.Archived));
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        await BuildJob(db, logger).ExecuteAsync();

        Assert.Equal(0, LoggedDueCount(logger));
        Assert.Equal(0, LoggedCreatedCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Count_Expired_Document_Even_When_Overdue()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        db.SharedCompanyDocuments.Add(
            CreateDoc(companyId, category.Id, Today.AddDays(-10), SharedCompanyDocumentStatus.Expired));
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        await BuildJob(db, logger).ExecuteAsync();

        Assert.Equal(0, LoggedDueCount(logger));
        Assert.Equal(0, LoggedCreatedCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Counts_Documents_Across_Multiple_Companies_In_A_Single_Run()
    {
        // This job queries across ALL companies (no CompanyId filter), unlike the per-company
        // ListSharedCompanyDocumentsDueForReviewHandler — confirm no accidental company-scoping crept in.
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var categoryA = await SeedCategoryAsync(db, companyA);
        var categoryB = await SeedCategoryAsync(db, companyB);

        db.SharedCompanyDocuments.AddRange(
            CreateDoc(companyA, categoryA.Id, Today.AddDays(-1)),
            CreateDoc(companyB, categoryB.Id, Today));
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        await BuildJob(db, logger).ExecuteAsync();

        Assert.Equal(2, LoggedDueCount(logger));
        Assert.Equal(0, LoggedCreatedCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Creates_Review_Task_For_Due_Document_With_A_Review_Owner()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var reviewOwnerId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        var doc = CreateDoc(companyId, category.Id, Today.AddDays(-5), reviewOwnerEmployeeId: reviewOwnerId);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        var taskCreator = new FakeTaskCreator();
        var notificationWriter = new FakeNotificationWriter();

        await BuildJob(db, logger, taskCreator, notificationWriter: notificationWriter).ExecuteAsync();

        var task = Assert.Single(taskCreator.Created);
        Assert.Equal(companyId, task.CompanyId);
        Assert.Equal(reviewOwnerId, task.AssignedEmployeeId);
        Assert.Equal(reviewOwnerId, task.AssignedUserId);
        Assert.Equal(doc.Id, task.SourceEntityId);
        Assert.Equal(TaskSource.Document, task.Source);
        Assert.Equal(TaskActionType.Review, task.ActionType);
        Assert.Equal(doc.ReviewDate, task.DueDate);

        // notifyAssignee must be false — the dedicated notification (asserted below) replaces the
        // generic "New task assigned" notification, which would have carried the task's own id as
        // SourceEntityId rather than the document's.
        Assert.False(task.NotifyAssignee);

        var notification = Assert.Single(notificationWriter.Written);
        Assert.Equal(companyId, notification.CompanyId);
        Assert.Equal(reviewOwnerId, notification.EmployeeId);
        Assert.Equal(doc.Id, notification.SourceEntityId);
        Assert.Equal(NotificationType.SharedCompanyDocumentReviewDue, notification.Type);

        Assert.Equal(1, LoggedDueCount(logger));
        Assert.Equal(1, LoggedCreatedCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Create_A_Task_For_A_Due_Document_With_No_Review_Owner()
    {
        // Deliberate "skip, don't fall back to another assignee" rule — a due document without a
        // configured review owner produces no task at all.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        db.SharedCompanyDocuments.Add(CreateDoc(companyId, category.Id, Today.AddDays(-5)));
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        var taskCreator = new FakeTaskCreator();

        await BuildJob(db, logger, taskCreator).ExecuteAsync();

        Assert.Empty(taskCreator.Created);
        Assert.Equal(0, LoggedCreatedCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Create_A_Second_Task_When_An_Open_Review_Task_Already_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var reviewOwnerId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        var doc = CreateDoc(companyId, category.Id, Today.AddDays(-5), reviewOwnerEmployeeId: reviewOwnerId);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        var taskCreator = new FakeTaskCreator();
        var openTaskReader = new FakeOpenTaskBySourceEntityReader();
        openTaskReader.AddOpenTask(doc.Id, TaskActionType.Review);
        var notificationWriter = new FakeNotificationWriter();

        await BuildJob(db, logger, taskCreator, openTaskReader, notificationWriter: notificationWriter).ExecuteAsync();

        Assert.Empty(taskCreator.Created);
        Assert.Equal(0, LoggedCreatedCount(logger));

        // The task-creation skip and the notification-write skip are gated by the same
        // `continue` inside the job, so an already-open Review task must suppress both.
        Assert.Empty(notificationWriter.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Still_Creates_A_Review_Task_When_The_Only_Open_Task_Is_A_Different_ActionType()
    {
        // KEY REGRESSION TEST: mirrors SharedCompanyDocumentAcknowledgementReminderJob creating an
        // open Acknowledge task with sourceEntityId = document.Id. Before the actionType-filtered
        // lookup was introduced, ANY open task for this source entity id (regardless of action
        // type) would have wrongly suppressed the new Review task. Prove that no longer happens.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var reviewOwnerId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        var doc = CreateDoc(companyId, category.Id, Today.AddDays(-5), reviewOwnerEmployeeId: reviewOwnerId);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        var taskCreator = new FakeTaskCreator();
        var openTaskReader = new FakeOpenTaskBySourceEntityReader();
        openTaskReader.AddOpenTask(doc.Id, TaskActionType.Acknowledge);

        await BuildJob(db, logger, taskCreator, openTaskReader).ExecuteAsync();

        var task = Assert.Single(taskCreator.Created);
        Assert.Equal(doc.Id, task.SourceEntityId);
        Assert.Equal(TaskActionType.Review, task.ActionType);
        Assert.Equal(reviewOwnerId, task.AssignedEmployeeId);

        Assert.Equal(1, LoggedCreatedCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Creates_A_Task_Per_Company_For_Multiple_Due_Documents_With_Different_Review_Owners()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var reviewOwnerA = Guid.NewGuid();
        var reviewOwnerB = Guid.NewGuid();
        var categoryA = await SeedCategoryAsync(db, companyA);
        var categoryB = await SeedCategoryAsync(db, companyB);

        var docA = CreateDoc(companyA, categoryA.Id, Today.AddDays(-1), reviewOwnerEmployeeId: reviewOwnerA);
        var docB = CreateDoc(companyB, categoryB.Id, Today, reviewOwnerEmployeeId: reviewOwnerB);
        db.SharedCompanyDocuments.AddRange(docA, docB);
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        var taskCreator = new FakeTaskCreator();

        await BuildJob(db, logger, taskCreator).ExecuteAsync();

        Assert.Equal(2, LoggedDueCount(logger));
        Assert.Equal(2, LoggedCreatedCount(logger));

        var taskA = Assert.Single(taskCreator.Created, t => t.SourceEntityId == docA.Id);
        Assert.Equal(companyA, taskA.CompanyId);
        Assert.Equal(reviewOwnerA, taskA.AssignedEmployeeId);

        var taskB = Assert.Single(taskCreator.Created, t => t.SourceEntityId == docB.Id);
        Assert.Equal(companyB, taskB.CompanyId);
        Assert.Equal(reviewOwnerB, taskB.AssignedEmployeeId);
    }
}
