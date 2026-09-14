using HR.SharedKernel.Idempotency;

namespace HR.SharedKernel.Tests;

/// <summary>
/// Ticket 3 (P1) final follow-up item 3: exercises the actual key-lifecycle rules used by both
/// AssetService's caller (EditPageBase) and LeaveService's caller (AdjustLeaveBalanceDialog), via
/// the shared <see cref="PendingIdempotentOperation"/> they're both built on - proving the
/// higher-level acceptance scenarios ("two concurrent operations use different keys", "retrying one
/// operation never reuses another's key") without needing a Blazor component test host.
/// </summary>
public class PendingIdempotentOperationTests
{
    private sealed record Payload(string Field);

    [Fact]
    public void Unchanged_Retry_Reuses_The_Same_Key()
    {
        var operation = new PendingIdempotentOperation();
        var payload = new Payload("same");

        var first = operation.PrepareKey(payload);
        // Simulates a lost response: no Complete() call, then the user clicks "Try again" with the
        // exact same (unchanged) request.
        var second = operation.PrepareKey(new Payload("same"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Changed_Payload_After_Ambiguous_Failure_Gets_A_New_Key()
    {
        var operation = new PendingIdempotentOperation();

        var first = operation.PrepareKey(new Payload("original"));
        // No Complete() - the first attempt was ambiguous - but the user edited the form before
        // retrying, so this must NOT be treated as a retry of the same operation.
        var second = operation.PrepareKey(new Payload("edited"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Completing_An_Operation_Means_The_Next_Attempt_Gets_A_New_Key_Even_With_The_Same_Payload()
    {
        var operation = new PendingIdempotentOperation();
        var payload = new Payload("same");

        var first = operation.PrepareKey(payload);
        operation.Complete(); // definitive outcome: success, or a rejection the user acted on

        var second = operation.PrepareKey(payload);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Two_Concurrent_Independent_Operations_Never_Share_A_Key()
    {
        // Simulates two independent dialogs/pages open at once (e.g. two asset-creation forms) -
        // each owns its OWN PendingIdempotentOperation instance, exactly as EditPageBase/the Leave
        // dialog do per component instance.
        var operationA = new PendingIdempotentOperation();
        var operationB = new PendingIdempotentOperation();

        var keyA = operationA.PrepareKey(new Payload("same"));
        var keyB = operationB.PrepareKey(new Payload("same"));

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void Retrying_One_Operation_Does_Not_Reuse_A_Key_Belonging_To_Another()
    {
        var operationA = new PendingIdempotentOperation();
        var operationB = new PendingIdempotentOperation();

        var keyA1 = operationA.PrepareKey(new Payload("a"));
        var keyB1 = operationB.PrepareKey(new Payload("b"));

        // A retries (its own ambiguous failure) - must still equal only its OWN prior key.
        var keyA2 = operationA.PrepareKey(new Payload("a"));

        Assert.Equal(keyA1, keyA2);
        Assert.NotEqual(keyA2, keyB1);
    }

    [Fact]
    public void Double_Clicking_Submit_Before_Any_Outcome_Uses_One_Logical_Operation()
    {
        // A double-click fires two PrepareKey calls for the exact same in-flight (not yet
        // Complete()d) submission before either has resolved - both must resolve to the same key.
        var operation = new PendingIdempotentOperation();
        var payload = new Payload("submitted-once");

        var clickOne = operation.PrepareKey(payload);
        var clickTwo = operation.PrepareKey(payload);

        Assert.Equal(clickOne, clickTwo);
    }
}
