using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.PurgeEligibleCandidates;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Ticket 23 (P2): durable correlation/causation/message-id metadata on
/// <see cref="CandidateDocumentDeletionOperation"/> — extends the pattern already established for
/// HR.Modules.Employees (the reference module; see
/// HR.Modules.Employees.Tests.AuditOutboxMetadataTests) to Recruitment's candidate document
/// deletion operation and its owning job (<see cref="PurgeCandidateDocumentStorageJob"/>).
/// </summary>
public class Ticket23CandidateDocumentDeletionOperationMetadataTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);


    [Fact]
    public void CreatePending_With_Supplied_Context_Stamps_CorrelationId_From_Context_CorrelationId_And_CausationId_From_Context_MessageId()
    {
        var context = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        var operation = CandidateDocumentDeletionOperation.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "company/candidate/doc/cv.pdf", Now, context);

        Assert.Equal(context.MessageId, operation.CorrelationId);
        Assert.Equal(context.MessageId, operation.CausationId);
        Assert.NotNull(operation.MessageId);
        Assert.NotEqual(Guid.Empty, operation.MessageId!.Value);
        Assert.NotEqual(context.MessageId, operation.MessageId!.Value);
    }

    [Fact]
    public void CreatePending_With_No_Context_Leaves_Correlation_And_Causation_Null_But_Still_Mints_A_Fresh_MessageId()
    {
        var operation = CandidateDocumentDeletionOperation.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "company/candidate/doc/cv.pdf", Now);

        Assert.Null(operation.CorrelationId);
        Assert.Null(operation.CausationId);
        Assert.NotNull(operation.MessageId);
        Assert.NotEqual(Guid.Empty, operation.MessageId!.Value);
    }


    private static Candidate CreateCandidateUpdatedAt(Guid companyId, DateTimeOffset updatedAt) =>
        Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, updatedAt);

    private static PurgeEligibleCandidatesHandler BuildHandler(
        RecruitmentDbContext db, Hangfire.IBackgroundJobClient? jobClient, IExecutionContextAccessor? accessor) =>
        new(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyRecruitmentSettingsReader(),
            new FakeLegalHoldStatusReader(), jobClient ?? new RecordingBackgroundJobClient(), accessor);

    [Fact]
    public async Task HandleAsync_With_Ambient_Context_Stamps_The_Created_Operations_Correlation_And_Causation_From_It()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldEnough = Now.AddDays(-731);
        var candidate = CreateCandidateUpdatedAt(companyId, oldEnough);
        var document = CandidateDocument.Create(
            Guid.NewGuid(), companyId, candidate.Id, "CV", "cv.pdf", 2048, "application/pdf",
            $"{companyId}/{candidate.Id}/{Guid.NewGuid():N}/cv.pdf", Guid.NewGuid(), oldEnough);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();

        var accessor = new ExecutionContextAccessor();
        var ambient = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);
        var jobClient = new RecordingBackgroundJobClient();

        bool wasSuccess;
        using (accessor.Push(ambient))
        {
            var handlerResult = await BuildHandler(db, jobClient, accessor).HandleAsync(
                new PurgeEligibleCandidatesRequest { CompanyId = companyId }, Guid.NewGuid(), CancellationToken.None);
            wasSuccess = handlerResult.IsSuccess;
        }

        Assert.True(wasSuccess);

        var operation = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.CandidateId == candidate.Id);
        Assert.Equal(ambient.MessageId, operation.CorrelationId);
        Assert.Equal(ambient.MessageId, operation.CausationId);
    }

    [Fact]
    public async Task HandleAsync_With_No_Accessor_Leaves_The_Created_Operations_Metadata_Null_Except_MessageId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldEnough = Now.AddDays(-731);
        var candidate = CreateCandidateUpdatedAt(companyId, oldEnough);
        var document = CandidateDocument.Create(
            Guid.NewGuid(), companyId, candidate.Id, "CV", "cv.pdf", 2048, "application/pdf",
            $"{companyId}/{candidate.Id}/{Guid.NewGuid():N}/cv.pdf", Guid.NewGuid(), oldEnough);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db, null, accessor: null).HandleAsync(
            new PurgeEligibleCandidatesRequest { CompanyId = companyId }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var operation = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.CandidateId == candidate.Id);
        Assert.Null(operation.CorrelationId);
        Assert.Null(operation.CausationId);
        Assert.NotNull(operation.MessageId);
    }


    private sealed class ContextCapturingCandidateDocumentStorageService(IExecutionContextAccessor accessor)
        : ICandidateDocumentStorageService
    {
        public IExecutionContext? ObservedDuringDelete { get; private set; }

        public string GenerateStorageKey(string storageFolder, string fileName)
            => throw new NotSupportedException();

        public Task UploadAsync(Stream content, string storageKey, string contentType, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Uri> GetDownloadUrlAsync(string storageKey, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            ObservedDuringDelete = accessor.Current;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ProcessAsync_Restores_Persisted_CorrelationId_CausationId_And_MessageId_As_The_Ambient_Context()
    {
        await using var db = BuildContext();
        var executionContext = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);
        var operation = CandidateDocumentDeletionOperation.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "company/candidate/doc/cv.pdf", Now, executionContext);
        db.CandidateDocumentDeletionOperations.Add(operation);
        await db.SaveChangesAsync();

        var accessor = new ExecutionContextAccessor();
        var storage = new ContextCapturingCandidateDocumentStorageService(accessor);
        var job = new PurgeCandidateDocumentStorageJob(
            db, storage, new FakeLegalHoldStatusReader(), new FakeAuditPublisher(),
            new FakeClock(FixedUtcNow), NullLogger<PurgeCandidateDocumentStorageJob>.Instance, accessor);

        await job.ProcessAsync(operation.Id);

        Assert.NotNull(storage.ObservedDuringDelete);
        var restored = storage.ObservedDuringDelete!;
        Assert.Equal(executionContext.CorrelationId, restored.CorrelationId);
        Assert.Equal(executionContext.MessageId, restored.CausationId);
        Assert.Equal(ExecutionOrigin.ReconciliationJob, restored.Origin);
    }

    [Fact]
    public async Task ProcessAsync_Legacy_Row_With_Null_Metadata_Still_Restores_A_Fresh_Root_Context_Rather_Than_Throwing()
    {
        await using var db = BuildContext();
        var operation = CandidateDocumentDeletionOperation.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "company/candidate/doc/cv.pdf", Now);
        db.CandidateDocumentDeletionOperations.Add(operation);
        await db.SaveChangesAsync();

        var accessor = new ExecutionContextAccessor();
        var storage = new ContextCapturingCandidateDocumentStorageService(accessor);
        var job = new PurgeCandidateDocumentStorageJob(
            db, storage, new FakeLegalHoldStatusReader(), new FakeAuditPublisher(),
            new FakeClock(FixedUtcNow), NullLogger<PurgeCandidateDocumentStorageJob>.Instance, accessor);

        var exception = await Record.ExceptionAsync(() => job.ProcessAsync(operation.Id));

        Assert.Null(exception);
        Assert.NotNull(storage.ObservedDuringDelete);
        Assert.Equal(ExecutionOrigin.ReconciliationJob, storage.ObservedDuringDelete!.Origin);
        Assert.Null(storage.ObservedDuringDelete.CausationId);
    }
}
