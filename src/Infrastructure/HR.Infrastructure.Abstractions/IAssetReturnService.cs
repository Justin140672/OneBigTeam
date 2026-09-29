namespace HR.Infrastructure.Abstractions;

public enum AssetReturnOutcome
{
    Returned,
    Lost,
    Damaged
}

public enum AssetReturnResult
{
    Success,

    NotFound,

    AlreadyReturned,

    EmployeeMismatch
}

public interface IAssetReturnService
{
    Task ReturnAsync(
        Guid companyId,
        Guid assignmentId,
        Guid returnedBy,
        CancellationToken cancellationToken,
        Guid dispatchOperationId = default);

    /// <summary>
    /// Verified return supporting a non-"Returned" outcome — used by callers (e.g. Offboarding) that
    /// must confirm the assignment actually belongs to a specific employee before mutating it, and
    /// that need to record a lost/damaged outcome distinct from a clean return.
    /// </summary>
    /// <param name="expectedEmployeeId">
    /// When supplied, the assignment must belong to this employee or the call fails with
    /// <see cref="AssetReturnResult.EmployeeMismatch"/> and no state is changed. Pass null to skip
    /// this check (equivalent to the unverified overload).
    /// </param>
    // dispatchOperationId (ticket 15, P1): stable Tasks-dispatch operation identity (Guid.Empty when
    // not applicable). When the assignment is already closed (AlreadyReturned) — e.g. a replayed
    // dispatch resuming after the primary Return() mutation committed but before its audit event
    // published — implementations recover the missed audit event rather than silently no-opping.
    Task<AssetReturnResult> ReturnAsync(
        Guid companyId,
        Guid assignmentId,
        Guid? expectedEmployeeId,
        AssetReturnOutcome outcome,
        Guid returnedBy,
        string? notes,
        CancellationToken cancellationToken,
        Guid dispatchOperationId = default);
}
