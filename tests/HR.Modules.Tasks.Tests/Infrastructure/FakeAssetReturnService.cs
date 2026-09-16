using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Tests.Infrastructure;

internal sealed class FakeAssetReturnService : IAssetReturnService
{
    public record Call(Guid CompanyId, Guid AssignmentId, Guid ReturnedBy);

    public record VerifiedCall(
        Guid CompanyId,
        Guid AssignmentId,
        Guid? ExpectedEmployeeId,
        AssetReturnOutcome Outcome,
        Guid ReturnedBy,
        string? Notes);

    public List<Call> Calls { get; } = [];
    public List<VerifiedCall> VerifiedCalls { get; } = [];
    public AssetReturnResult NextResult { get; set; } = AssetReturnResult.Success;

    public Task ReturnAsync(
        Guid companyId, Guid assignmentId, Guid returnedBy, CancellationToken cancellationToken,
        Guid dispatchOperationId = default)
    {
        Calls.Add(new Call(companyId, assignmentId, returnedBy));
        return Task.CompletedTask;
    }

    public Task<AssetReturnResult> ReturnAsync(
        Guid companyId,
        Guid assignmentId,
        Guid? expectedEmployeeId,
        AssetReturnOutcome outcome,
        Guid returnedBy,
        string? notes,
        CancellationToken cancellationToken,
        Guid dispatchOperationId = default)
    {
        VerifiedCalls.Add(new VerifiedCall(companyId, assignmentId, expectedEmployeeId, outcome, returnedBy, notes));
        return Task.FromResult(NextResult);
    }
}
