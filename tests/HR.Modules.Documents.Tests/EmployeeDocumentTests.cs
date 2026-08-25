using HR.Modules.Documents.Domain;

namespace HR.Modules.Documents.Tests;

public class EmployeeDocumentTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private static EmployeeDocument CreateDocument(DateOnly? expiryDate = null) =>
        EmployeeDocument.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            CreatedAt, issueDate: null, expiryDate: expiryDate);

    // ── MarkExpiryReminderSent ──────────────────────────────────────────────────

    [Fact]
    public void MarkExpiryReminderSent_NinetyDays_Sets_Only_ExpiryReminder90SentAt()
    {
        var doc = CreateDocument(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(90));
        var now = CreatedAt.AddDays(1);

        doc.MarkExpiryReminderSent(ExpiryReminderStage.NinetyDays, now);

        Assert.Equal(now, doc.ExpiryReminder90SentAt);
        Assert.Null(doc.ExpiryReminder30SentAt);
        Assert.Null(doc.ExpiryReminder7SentAt);
        Assert.Equal(now, doc.UpdatedAt);
    }

    [Fact]
    public void MarkExpiryReminderSent_ThirtyDays_Sets_Only_ExpiryReminder30SentAt()
    {
        var doc = CreateDocument();
        var now = CreatedAt.AddDays(1);

        doc.MarkExpiryReminderSent(ExpiryReminderStage.ThirtyDays, now);

        Assert.Null(doc.ExpiryReminder90SentAt);
        Assert.Equal(now, doc.ExpiryReminder30SentAt);
        Assert.Null(doc.ExpiryReminder7SentAt);
        Assert.Equal(now, doc.UpdatedAt);
    }

    [Fact]
    public void MarkExpiryReminderSent_SevenDays_Sets_Only_ExpiryReminder7SentAt()
    {
        var doc = CreateDocument();
        var now = CreatedAt.AddDays(1);

        doc.MarkExpiryReminderSent(ExpiryReminderStage.SevenDays, now);

        Assert.Null(doc.ExpiryReminder90SentAt);
        Assert.Null(doc.ExpiryReminder30SentAt);
        Assert.Equal(now, doc.ExpiryReminder7SentAt);
        Assert.Equal(now, doc.UpdatedAt);
    }

    [Fact]
    public void MarkExpiryReminderSent_Does_Not_Clear_Previously_Set_Stages()
    {
        var doc = CreateDocument();
        var first  = CreatedAt.AddDays(1);
        var second = CreatedAt.AddDays(2);

        doc.MarkExpiryReminderSent(ExpiryReminderStage.NinetyDays, first);
        doc.MarkExpiryReminderSent(ExpiryReminderStage.ThirtyDays, second);

        Assert.Equal(first,  doc.ExpiryReminder90SentAt);
        Assert.Equal(second, doc.ExpiryReminder30SentAt);
        Assert.Null(doc.ExpiryReminder7SentAt);
        Assert.Equal(second, doc.UpdatedAt);
    }

    // ── UpdateExpiryDate ─────────────────────────────────────────────────────────

    [Fact]
    public void UpdateExpiryDate_Resets_All_Reminder_State_When_All_Were_Previously_Set()
    {
        var doc = CreateDocument(DateOnly.FromDateTime(DateTime.UtcNow));
        var setAt = CreatedAt.AddDays(1);

        doc.MarkExpiryReminderSent(ExpiryReminderStage.NinetyDays, setAt);
        doc.MarkExpiryReminderSent(ExpiryReminderStage.ThirtyDays, setAt);
        doc.MarkExpiryReminderSent(ExpiryReminderStage.SevenDays, setAt);
        doc.MarkExpiringSoonNotified(setAt);
        doc.MarkExpiredNotified(setAt);

        var newExpiry = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(180);
        var now = CreatedAt.AddDays(2);

        doc.UpdateExpiryDate(newExpiry, now);

        Assert.Equal(newExpiry, doc.ExpiryDate);
        Assert.Null(doc.ExpiryReminder90SentAt);
        Assert.Null(doc.ExpiryReminder30SentAt);
        Assert.Null(doc.ExpiryReminder7SentAt);
        Assert.Null(doc.ExpiringSoonNotifiedAt);
        Assert.Null(doc.ExpiredNotifiedAt);
        Assert.Equal(now, doc.UpdatedAt);
    }

    [Fact]
    public void UpdateExpiryDate_Resets_All_Reminder_State_When_Setting_ExpiryDate_To_Null()
    {
        var doc = CreateDocument(DateOnly.FromDateTime(DateTime.UtcNow));
        var setAt = CreatedAt.AddDays(1);

        doc.MarkExpiryReminderSent(ExpiryReminderStage.NinetyDays, setAt);
        doc.MarkExpiryReminderSent(ExpiryReminderStage.ThirtyDays, setAt);
        doc.MarkExpiryReminderSent(ExpiryReminderStage.SevenDays, setAt);
        doc.MarkExpiringSoonNotified(setAt);
        doc.MarkExpiredNotified(setAt);

        var now = CreatedAt.AddDays(2);

        doc.UpdateExpiryDate(null, now);

        Assert.Null(doc.ExpiryDate);
        Assert.Null(doc.ExpiryReminder90SentAt);
        Assert.Null(doc.ExpiryReminder30SentAt);
        Assert.Null(doc.ExpiryReminder7SentAt);
        Assert.Null(doc.ExpiringSoonNotifiedAt);
        Assert.Null(doc.ExpiredNotifiedAt);
        Assert.Equal(now, doc.UpdatedAt);
    }
}
