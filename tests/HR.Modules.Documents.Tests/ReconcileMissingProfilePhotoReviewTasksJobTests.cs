using HR.Modules.Tasks.Contracts;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Documents.Tests;

public class ReconcileMissingProfilePhotoReviewTasksJobTests
{
    private static DocumentsDbContext BuildDbContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class FakeEmployeeNameReader(Dictionary<Guid, string>? names = null) : IEmployeeNameReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(names ?? new Dictionary<Guid, string>());
    }

    private sealed class ThrowingTaskCreator(FakeTaskCreator inner, HashSet<Guid> failFor) : ITaskCreator
    {
        public Task<Guid> CreateAsync(
            Guid companyId,
            Guid createdBy,
            string title,
            string? description,
            TaskPriority priority,
            TaskSource source,
            TaskActionType actionType,
            DateOnly? dueDate,
            Guid? assignedEmployeeId,
            Guid? assignedUserId,
            Guid? sourceEntityId,
            CancellationToken cancellationToken,
            bool notifyAssignee = true,
            string? idempotencyKey = null)
        {
            if (sourceEntityId is not null && failFor.Contains(sourceEntityId.Value))
                throw new InvalidOperationException("Simulated reconciliation failure.");

            return inner.CreateAsync(
                companyId, createdBy, title, description, priority, source, actionType,
                dueDate, assignedEmployeeId, assignedUserId, sourceEntityId, cancellationToken,
                notifyAssignee, idempotencyKey);
        }
    }

    private static PendingProfilePhoto SeedPendingPhoto(
        DocumentsDbContext db, Guid companyId, Guid employeeId, string fileName = "pending.png") =>
        SeedPendingPhoto(db, companyId, employeeId, Guid.NewGuid(), fileName);

    private static PendingProfilePhoto SeedPendingPhoto(
        DocumentsDbContext db, Guid companyId, Guid employeeId, Guid id, string fileName)
    {
        var pending = PendingProfilePhoto.Create(
            id, companyId, employeeId, fileName, 111, "image/png",
            $"pending/{id}.png", employeeId, DateTimeOffset.UtcNow);
        db.PendingProfilePhotos.Add(pending);
        db.SaveChanges();
        return pending;
    }

    [Fact]
    public async Task ExecuteAsync_With_No_Pending_Photos_Makes_No_CreateAsync_Calls()
    {
        var db          = BuildDbContext();
        var taskCreator = new FakeTaskCreator();
        var job = new ReconcileMissingProfilePhotoReviewTasksJob(
            db, taskCreator, new FakeEmployeeNameReader(), NullLogger<ReconcileMissingProfilePhotoReviewTasksJob>.Instance);

        await job.ExecuteAsync();

        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task ExecuteAsync_With_One_Pending_Photo_Creates_Exactly_One_Task_With_Correct_IdempotencyKey_And_SourceEntityId()
    {
        var db          = BuildDbContext();
        var companyId   = Guid.NewGuid();
        var employeeId  = Guid.NewGuid();
        var pending     = SeedPendingPhoto(db, companyId, employeeId);
        var taskCreator = new FakeTaskCreator();
        var job = new ReconcileMissingProfilePhotoReviewTasksJob(
            db, taskCreator, new FakeEmployeeNameReader(), NullLogger<ReconcileMissingProfilePhotoReviewTasksJob>.Instance);

        await job.ExecuteAsync();

        var created = Assert.Single(taskCreator.Created);
        Assert.Equal(companyId,             created.CompanyId);
        Assert.Equal(pending.Id,            created.SourceEntityId);
        Assert.Equal(TaskSource.Document,   created.Source);
        Assert.Equal(TaskActionType.Review, created.ActionType);
        Assert.Equal($"ProfilePhotoReview:{pending.Id}", created.IdempotencyKey);
    }

    [Fact]
    public async Task ExecuteAsync_With_Pending_Photos_In_Two_Companies_Creates_A_Scoped_Call_Per_Company()
    {
        var db = BuildDbContext();
        var companyA  = Guid.NewGuid();
        var companyB  = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var pendingA  = SeedPendingPhoto(db, companyA, employeeA, "a.png");
        var pendingB  = SeedPendingPhoto(db, companyB, employeeB, "b.png");
        var taskCreator = new FakeTaskCreator();
        var job = new ReconcileMissingProfilePhotoReviewTasksJob(
            db, taskCreator, new FakeEmployeeNameReader(), NullLogger<ReconcileMissingProfilePhotoReviewTasksJob>.Instance);

        await job.ExecuteAsync();

        Assert.Equal(2, taskCreator.Created.Count);
        Assert.Contains(taskCreator.Created, c => c.CompanyId == companyA && c.SourceEntityId == pendingA.Id);
        Assert.Contains(taskCreator.Created, c => c.CompanyId == companyB && c.SourceEntityId == pendingB.Id);
    }

    [Fact]
    public async Task ExecuteAsync_One_Submission_Throwing_Does_Not_Prevent_Others_In_The_Batch_From_Being_Processed()
    {
        var db = BuildDbContext();
        var companyId  = Guid.NewGuid();
        var failing    = SeedPendingPhoto(db, companyId, Guid.NewGuid(), "failing.png");
        var succeeding = SeedPendingPhoto(db, companyId, Guid.NewGuid(), "succeeding.png");

        var innerCreator = new FakeTaskCreator();
        var throwingCreator = new ThrowingTaskCreator(innerCreator, [failing.Id]);
        var job = new ReconcileMissingProfilePhotoReviewTasksJob(
            db, throwingCreator, new FakeEmployeeNameReader(),
            NullLogger<ReconcileMissingProfilePhotoReviewTasksJob>.Instance);

        await job.ExecuteAsync();

        var created = Assert.Single(innerCreator.Created);
        Assert.Equal(succeeding.Id, created.SourceEntityId);
    }
}
