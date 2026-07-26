using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.AcknowledgeSharedCompanyDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class AcknowledgeSharedCompanyDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Creates_An_Acknowledgement_Row()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.VersionNumber);

        var saved = await db.SharedCompanyDocumentAcknowledgements.SingleAsync();
        Assert.Equal(caller, saved.EmployeeId);
        Assert.Equal(1,      saved.VersionNumber);
    }

    [Fact]
    public async Task HandleAsync_Publishes_An_Acknowledged_Audit_Event_For_A_New_Acknowledgement()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        await Handler(db, auditPublisher: auditPublisher).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        var published = Assert.Single(auditPublisher.Published.OfType<SharedCompanyDocumentAcknowledgedAuditEvent>());
        Assert.Equal(1, published.VersionNumber);
        Assert.Equal(caller, published.AcknowledgedBy);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_A_Second_Audit_Event_When_Acknowledging_Idempotently()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var handler = Handler(db, auditPublisher: auditPublisher);
        await handler.HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);
        await handler.HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.Single(auditPublisher.Published.OfType<SharedCompanyDocumentAcknowledgedAuditEvent>());
    }

    [Fact]
    public async Task HandleAsync_Is_Idempotent_For_The_Same_Version()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var handler = Handler(db);
        await handler.HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);
        var second = await handler.HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Single(db.SharedCompanyDocumentAcknowledgements);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Document_Does_Not_Require_Acknowledgement()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: false, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Draft_Document()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Caller_Outside_Audience()
    {
        await using var db = BuildContext();
        var companyId    = Guid.NewGuid();
        var category     = await SeedCategory(db, companyId);
        var departmentId = Guid.NewGuid();
        var caller        = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.Add(SharedCompanyDocumentAudienceRule.Create(
            Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Department, departmentId));
        await db.SaveChangesAsync();

        // Caller has no seeded audience entry, so their department is null — doesn't match.
        var result = await Handler(db).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Reacknowledging_After_A_New_Version_Preserves_The_Old_Acknowledgement()
    {
        // The core "version preservation" guarantee: replacing the file must never touch the
        // acknowledgement row already recorded against the version the employee actually saw —
        // it stays exactly as it was, at VersionNumber 1, alongside the new row at VersionNumber 2.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var handler = Handler(db);
        var first = await handler.HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        var stored = await db.SharedCompanyDocuments.SingleAsync();
        stored.ReplaceFile("key/v2.pdf", "v2.pdf", 200, "application/pdf", Guid.NewGuid(), Now.AddDays(1));
        await db.SaveChangesAsync();

        var second = await handler.HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(1, first.Value!.VersionNumber);
        Assert.Equal(2, second.Value!.VersionNumber);

        var rows = await db.SharedCompanyDocumentAcknowledgements
            .OrderBy(a => a.VersionNumber)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].VersionNumber);
        Assert.Equal(caller, rows[0].EmployeeId);
        Assert.Equal(2, rows[1].VersionNumber);
        Assert.Equal(caller, rows[1].EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Captures_The_Statement_As_Shown_At_Acknowledgement_Time()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, acknowledgementDueDate: null,
            acknowledgementStatement: "I confirm I have read the updated expenses policy.",
            createdBy: Guid.NewGuid(), now: Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        await Handler(db).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        var saved = await db.SharedCompanyDocumentAcknowledgements.SingleAsync();
        Assert.Equal("I confirm I have read the updated expenses policy.", saved.AcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_Captures_The_Default_Statement_When_Document_Has_No_Custom_One()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        await Handler(db).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        var saved = await db.SharedCompanyDocumentAcknowledgements.SingleAsync();
        Assert.Equal("I confirm that I have read and understood this document.", saved.AcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_Captures_The_Related_TaskId_When_Provided()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var taskId     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        await Handler(db).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id, TaskId = taskId }, caller,
            CancellationToken.None);

        var saved = await db.SharedCompanyDocumentAcknowledgements.SingleAsync();
        Assert.Equal(taskId, saved.TaskId);
    }

    [Fact]
    public async Task HandleAsync_TaskId_Is_Null_When_Not_Reached_Via_A_Task()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        await Handler(db).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        var saved = await db.SharedCompanyDocumentAcknowledgements.SingleAsync();
        Assert.Null(saved.TaskId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Overwrite_The_Recorded_Statement_When_Acknowledged_Again_Idempotently()
    {
        // Immutability in practice: the idempotent re-acknowledge path must return the existing
        // row untouched, even if the document's statement has since been edited — the row keeps
        // showing exactly what the employee agreed to at the time.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, acknowledgementDueDate: null,
            acknowledgementStatement: "Original statement.", createdBy: Guid.NewGuid(), now: Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var handler = Handler(db);
        await handler.HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        var stored = await db.SharedCompanyDocuments.SingleAsync();
        stored.SetAcknowledgementSettings(true, null, "Changed statement.", Guid.NewGuid(), Now.AddDays(1));
        await db.SaveChangesAsync();

        await handler.HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        var saved = await db.SharedCompanyDocumentAcknowledgements.SingleAsync();
        Assert.Equal("Original statement.", saved.AcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_CompletesTheCallingEmployeesOwnAcknowledgeTask()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var taskCompleter = new FakeTaskCompleter();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db, taskCompleter: taskCompleter).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var call = Assert.Single(taskCompleter.ForEmployeeCalls);
        Assert.Equal(companyId,                  call.CompanyId);
        Assert.Equal(doc.Id,                      call.SourceEntityId);
        Assert.Equal(TaskSource.Document,         call.Source);
        Assert.Equal(TaskActionType.Acknowledge,  call.ActionType);
        Assert.Equal(caller,                      call.AssignedEmployeeId);
        Assert.Equal(caller,                      call.CompletedBy);
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyAcknowledgedIdempotently_DoesNotCallTaskCompleterAgain()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var taskCompleter = new FakeTaskCompleter();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var request = new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id };
        await Handler(db, taskCompleter: taskCompleter).HandleAsync(request, caller, CancellationToken.None);
        await Handler(db, taskCompleter: taskCompleter).HandleAsync(request, caller, CancellationToken.None);

        Assert.Equal(1, taskCompleter.ForEmployeeCallCount);
    }

    [Fact]
    public async Task HandleAsync_Persists_IsConfirmed_True_When_Request_Confirmed_Is_True()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id, Confirmed = true }, caller,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SharedCompanyDocumentAcknowledgements.SingleAsync();
        Assert.True(saved.IsConfirmed);
    }

    [Fact]
    public async Task HandleAsync_Persists_IsConfirmed_False_When_Request_Confirmed_Is_False()
    {
        // The handler itself doesn't reject an unconfirmed request — that's the validator's job —
        // so this succeeds and simply stores the flag as false.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id, Confirmed = false }, caller,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SharedCompanyDocumentAcknowledgements.SingleAsync();
        Assert.False(saved.IsConfirmed);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_With_IsConfirmed_And_AcknowledgementStatement()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, acknowledgementDueDate: null,
            acknowledgementStatement: "I confirm I have read the updated expenses policy.",
            createdBy: Guid.NewGuid(), now: Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        await Handler(db, auditPublisher: auditPublisher).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id, Confirmed = true }, caller,
            CancellationToken.None);

        var published = Assert.Single(auditPublisher.Published.OfType<SharedCompanyDocumentAcknowledgedAuditEvent>());
        Assert.True(published.IsConfirmed);
        Assert.Equal("I confirm I have read the updated expenses policy.", published.AcknowledgementStatement);

        // The After payload — not just the record's own properties — is what actually gets
        // rendered by GetSharedCompanyDocumentAuditHistoryHandler's BuildChanges, so assert its
        // shape directly: VersionNumber, IsConfirmed and AcknowledgementStatement all now live in
        // After (not Metadata, which is null for this event).
        IAuditEvent auditEvent = published;
        var afterJson = JsonSerializer.SerializeToElement(auditEvent.After);
        Assert.Equal(1, afterJson.GetProperty("VersionNumber").GetInt32());
        Assert.True(afterJson.GetProperty("IsConfirmed").GetBoolean());
        Assert.Equal("I confirm I have read the updated expenses policy.", afterJson.GetProperty("AcknowledgementStatement").GetString());
        Assert.Null(auditEvent.Metadata);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_With_IsConfirmed_False_When_Not_Confirmed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        await Handler(db, auditPublisher: auditPublisher).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id, Confirmed = false }, caller,
            CancellationToken.None);

        var published = Assert.Single(auditPublisher.Published.OfType<SharedCompanyDocumentAcknowledgedAuditEvent>());
        Assert.False(published.IsConfirmed);
    }

    [Fact]
    public async Task HandleAsync_Publishes_SharedCompanyDocumentAcknowledged_IntegrationEvent_For_A_New_Acknowledgement()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var integrationPublisher = new CapturingIntegrationEventPublisher();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Expenses Policy", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db, integrationEventPublisher: integrationPublisher).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<SharedCompanyDocumentAcknowledgedIntegrationEvent>(Assert.Single(integrationPublisher.Published));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(caller, evt.EmployeeId);
        Assert.Equal(doc.Id, evt.DocumentId);
        Assert.Equal("Expenses Policy", evt.DocumentTitle);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_A_Second_IntegrationEvent_When_Acknowledging_Idempotently()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var integrationPublisher = new CapturingIntegrationEventPublisher();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var handler = Handler(db, integrationEventPublisher: integrationPublisher);
        var request = new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id };
        await handler.HandleAsync(request, caller, CancellationToken.None);
        await handler.HandleAsync(request, caller, CancellationToken.None);

        Assert.Single(integrationPublisher.Published);
    }

    private static AcknowledgeSharedCompanyDocumentHandler Handler(
        DocumentsDbContext db,
        FakeEmployeeAudienceReader? audienceReader = null,
        FakeTaskCompleter? taskCompleter = null,
        FakeAuditPublisher? auditPublisher = null,
        IIntegrationEventPublisher? integrationEventPublisher = null) =>
        new(db, new SharedCompanyDocumentAudienceMatcher(db, audienceReader ?? new FakeEmployeeAudienceReader()),
            taskCompleter ?? new FakeTaskCompleter(),
            auditPublisher ?? new FakeAuditPublisher(),
            new FakeCompanyAcknowledgementSettingsReader(),
            new FakeClock(FixedUtcNow),
            integrationEventPublisher ?? new NoOpIntegrationEventPublisher());

    private static async Task<CompanyDocumentCategory> SeedCategory(
        DocumentsDbContext db, Guid companyId, string name = "Policy")
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, name, Now);
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
