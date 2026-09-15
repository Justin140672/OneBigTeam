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

    // Bug fix regression (P1 follow-up to Ticket 3): production callers pass C# value tuples as the
    // snapshot - e.g. AdjustLeaveBalanceDialog.razor calls
    // `_submitOperation.PrepareKey((CompanyId, EmployeeId, request))`. Value tuples expose their
    // contents as PUBLIC FIELDS, not properties, which is exactly the shape that previously
    // fingerprinted to "{}" under default System.Text.Json options and never rotated the key.
    private enum LeaveBalanceAdjustmentReason { Correction, Other }

    private sealed record AdjustLeaveBalanceModel(
        Guid LeaveTypeId,
        decimal AdjustmentValue,
        LeaveBalanceAdjustmentReason Reason,
        string? Comments,
        bool AllowNegativeOverride);

    private static (Guid CompanyId, Guid EmployeeId, AdjustLeaveBalanceModel Request) BuildTupleSnapshot(
        Guid companyId, Guid employeeId, AdjustLeaveBalanceModel request) => (companyId, employeeId, request);

    [Fact]
    public void Tuple_Snapshot_Unchanged_Retry_Reuses_The_Same_Key()
    {
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var request = new AdjustLeaveBalanceModel(leaveTypeId, 1m, LeaveBalanceAdjustmentReason.Correction, "note", false);

        var first = operation.PrepareKey(BuildTupleSnapshot(companyId, employeeId, request));
        // Unchanged retry: a brand-new tuple/record instance with identical values.
        var second = operation.PrepareKey(BuildTupleSnapshot(
            companyId, employeeId,
            new AdjustLeaveBalanceModel(leaveTypeId, 1m, LeaveBalanceAdjustmentReason.Correction, "note", false)));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Tuple_Snapshot_Changed_AdjustmentValue_Rotates_The_Key()
    {
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();

        var first = operation.PrepareKey(BuildTupleSnapshot(
            companyId, employeeId, new AdjustLeaveBalanceModel(leaveTypeId, 1m, LeaveBalanceAdjustmentReason.Correction, "note", false)));
        var second = operation.PrepareKey(BuildTupleSnapshot(
            companyId, employeeId, new AdjustLeaveBalanceModel(leaveTypeId, 2m, LeaveBalanceAdjustmentReason.Correction, "note", false)));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Tuple_Snapshot_Changed_Reason_Rotates_The_Key()
    {
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();

        var first = operation.PrepareKey(BuildTupleSnapshot(
            companyId, employeeId, new AdjustLeaveBalanceModel(leaveTypeId, 1m, LeaveBalanceAdjustmentReason.Correction, "note", false)));
        var second = operation.PrepareKey(BuildTupleSnapshot(
            companyId, employeeId, new AdjustLeaveBalanceModel(leaveTypeId, 1m, LeaveBalanceAdjustmentReason.Other, "note", false)));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Tuple_Snapshot_Changed_Comments_Rotates_The_Key()
    {
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();

        var first = operation.PrepareKey(BuildTupleSnapshot(
            companyId, employeeId, new AdjustLeaveBalanceModel(leaveTypeId, 1m, LeaveBalanceAdjustmentReason.Correction, "note", false)));
        var second = operation.PrepareKey(BuildTupleSnapshot(
            companyId, employeeId, new AdjustLeaveBalanceModel(leaveTypeId, 1m, LeaveBalanceAdjustmentReason.Correction, "different note", false)));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Tuple_Snapshot_Changed_AllowNegativeOverride_Rotates_The_Key()
    {
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();

        var first = operation.PrepareKey(BuildTupleSnapshot(
            companyId, employeeId, new AdjustLeaveBalanceModel(leaveTypeId, 1m, LeaveBalanceAdjustmentReason.Correction, "note", false)));
        var second = operation.PrepareKey(BuildTupleSnapshot(
            companyId, employeeId, new AdjustLeaveBalanceModel(leaveTypeId, 1m, LeaveBalanceAdjustmentReason.Correction, "note", true)));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Tuple_Snapshot_Changed_LeaveTypeId_Rotates_The_Key()
    {
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var first = operation.PrepareKey(BuildTupleSnapshot(
            companyId, employeeId, new AdjustLeaveBalanceModel(Guid.NewGuid(), 1m, LeaveBalanceAdjustmentReason.Correction, "note", false)));
        var second = operation.PrepareKey(BuildTupleSnapshot(
            companyId, employeeId, new AdjustLeaveBalanceModel(Guid.NewGuid(), 1m, LeaveBalanceAdjustmentReason.Correction, "note", false)));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Tuple_Snapshot_Changed_EmployeeId_Rotates_The_Key()
    {
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var request = new AdjustLeaveBalanceModel(leaveTypeId, 1m, LeaveBalanceAdjustmentReason.Correction, "note", false);

        var first = operation.PrepareKey(BuildTupleSnapshot(companyId, Guid.NewGuid(), request));
        var second = operation.PrepareKey(BuildTupleSnapshot(companyId, Guid.NewGuid(), request));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Tuple_Snapshot_Changed_CompanyId_Rotates_The_Key()
    {
        var operation = new PendingIdempotentOperation();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var request = new AdjustLeaveBalanceModel(leaveTypeId, 1m, LeaveBalanceAdjustmentReason.Correction, "note", false);

        var first = operation.PrepareKey(BuildTupleSnapshot(Guid.NewGuid(), employeeId, request));
        var second = operation.PrepareKey(BuildTupleSnapshot(Guid.NewGuid(), employeeId, request));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Tuple_Snapshot_Two_Independent_Operations_Given_The_Identical_Tuple_Get_Different_Keys()
    {
        var operationA = new PendingIdempotentOperation();
        var operationB = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var request = new AdjustLeaveBalanceModel(Guid.NewGuid(), 1m, LeaveBalanceAdjustmentReason.Correction, "note", false);
        var snapshot = BuildTupleSnapshot(companyId, employeeId, request);

        var keyA = operationA.PrepareKey(snapshot);
        var keyB = operationB.PrepareKey(snapshot);

        Assert.NotEqual(keyA, keyB);
    }

    // Mirrors EditPageBase.cs's create-operation call site: `_createOperation.PrepareKey((GetCompanyId(), Model))`
    // - a 2-tuple whose second element is a mutable class/record with public PROPERTIES (not the
    // tuple's own fields). Exercised separately from the 3-tuple case above because the fix must
    // cover both "properties nested inside a tuple field" and "the tuple's own fields" changing.
    private sealed class AssetEditModel
    {
        public string? AssetNumber { get; set; }
        public string? Name { get; set; }
        public decimal? PurchasePrice { get; set; }
    }

    [Fact]
    public void TwoTuple_Snapshot_Changed_Model_Property_Rotates_The_Key()
    {
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var model = new AssetEditModel { AssetNumber = "A-001", Name = "Laptop", PurchasePrice = 1000m };

        var first = operation.PrepareKey((companyId, model));

        model.PurchasePrice = 1200m; // user edited the form before retrying after an ambiguous failure
        var second = operation.PrepareKey((companyId, model));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TwoTuple_Snapshot_Unchanged_Model_Reuses_The_Same_Key()
    {
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var model = new AssetEditModel { AssetNumber = "A-001", Name = "Laptop", PurchasePrice = 1000m };

        var first = operation.PrepareKey((companyId, model));
        var second = operation.PrepareKey((companyId, model));

        Assert.Equal(first, second);
    }

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
