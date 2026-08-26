using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class OutboxMessageTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly DateTimeOffset CreatedAt = new(new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc));

    private static OutboxMessage CreatePending() => OutboxMessage.CreatePending(
        Guid.NewGuid(), CompanyId, "employee-numbering.reformat-requested", "{}", CreatedAt);

    [Fact]
    public void CreatePending_Sets_Pending_Status_And_Zero_AttemptCount()
    {
        var message = CreatePending();

        Assert.Equal(OutboxMessage.StatusPending, message.Status);
        Assert.Equal(0, message.AttemptCount);
        Assert.Equal(CreatedAt, message.CreatedAt);
        Assert.Null(message.ProcessedAt);
        Assert.Null(message.FailedAt);
        Assert.Null(message.ErrorMessage);
    }

    [Fact]
    public void MarkProcessing_Sets_Status_And_Increments_AttemptCount_Each_Call()
    {
        var message = CreatePending();
        var firstAttempt = CreatedAt.AddMinutes(1);
        var secondAttempt = CreatedAt.AddMinutes(2);

        message.MarkProcessing(firstAttempt);

        Assert.Equal(OutboxMessage.StatusProcessing, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal(firstAttempt, message.LastAttemptAt);

        message.MarkProcessing(secondAttempt);

        Assert.Equal(OutboxMessage.StatusProcessing, message.Status);
        Assert.Equal(2, message.AttemptCount);
        Assert.Equal(secondAttempt, message.LastAttemptAt);
    }

    [Fact]
    public void MarkProcessing_Clears_Any_Previous_ErrorMessage()
    {
        var message = CreatePending();
        message.MarkProcessing(CreatedAt.AddMinutes(1));
        message.MarkFailed("boom", CreatedAt.AddMinutes(2));
        Assert.NotNull(message.ErrorMessage);

        message.MarkProcessing(CreatedAt.AddMinutes(3));

        Assert.Null(message.ErrorMessage);
        Assert.Equal(OutboxMessage.StatusProcessing, message.Status);
    }

    [Fact]
    public void MarkProcessed_Sets_Status_ProcessedAt_And_Clears_ErrorMessage()
    {
        var message = CreatePending();
        message.MarkProcessing(CreatedAt.AddMinutes(1));
        var processedAt = CreatedAt.AddMinutes(2);

        message.MarkProcessed(processedAt);

        Assert.Equal(OutboxMessage.StatusProcessed, message.Status);
        Assert.Equal(processedAt, message.ProcessedAt);
        Assert.Null(message.ErrorMessage);
    }

    [Fact]
    public void MarkFailed_Sets_Status_FailedAt_And_ErrorMessage()
    {
        var message = CreatePending();
        message.MarkProcessing(CreatedAt.AddMinutes(1));
        var failedAt = CreatedAt.AddMinutes(2);

        message.MarkFailed("Employee renumbering failed.", failedAt);

        Assert.Equal(OutboxMessage.StatusFailed, message.Status);
        Assert.Equal(failedAt, message.FailedAt);
        Assert.Equal("Employee renumbering failed.", message.ErrorMessage);
    }

    [Fact]
    public void ResetForRetry_From_Failed_Resets_To_Pending_Clears_FailedAt_And_ErrorMessage_But_Preserves_AttemptCount()
    {
        var message = CreatePending();
        message.MarkProcessing(CreatedAt.AddMinutes(1));
        message.MarkProcessing(CreatedAt.AddMinutes(2));
        message.MarkFailed("Employee renumbering failed.", CreatedAt.AddMinutes(3));
        Assert.Equal(2, message.AttemptCount);

        var resetAt = CreatedAt.AddMinutes(4);
        message.ResetForRetry(resetAt);

        Assert.Equal(OutboxMessage.StatusPending, message.Status);
        Assert.Null(message.FailedAt);
        Assert.Null(message.ErrorMessage);
        // AttemptCount is deliberately NOT reset — it reflects total attempts made across retries.
        Assert.Equal(2, message.AttemptCount);
    }

    [Fact]
    public void ResetForRetry_From_Pending_Throws()
    {
        var message = CreatePending();

        Assert.Throws<InvalidOperationException>(() => message.ResetForRetry(CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void ResetForRetry_From_Processing_Throws()
    {
        var message = CreatePending();
        message.MarkProcessing(CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => message.ResetForRetry(CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void ResetForRetry_From_Processed_Throws()
    {
        var message = CreatePending();
        message.MarkProcessing(CreatedAt.AddMinutes(1));
        message.MarkProcessed(CreatedAt.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(() => message.ResetForRetry(CreatedAt.AddMinutes(3)));
    }
}
