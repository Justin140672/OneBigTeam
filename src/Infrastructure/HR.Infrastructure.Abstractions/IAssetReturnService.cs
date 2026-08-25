namespace HR.Infrastructure.Abstractions;

/// <summary>
/// The outcome of completing an asset return. <see cref="Returned"/> means the asset came back in a
/// reusable condition and the underlying Asset is made available again; <see cref="Lost"/> and
/// <see cref="Damaged"/> record a non-returnable outcome — the assignment is still closed (so it no
/// longer blocks offboarding/task completion) but the Asset is left unavailable for reassignment
/// pending HR review, rather than being marked Available.
/// </summary>
public enum AssetReturnOutcome
{
    Returned,
    Lost,
    Damaged
}

/// <summary>
/// Result of attempting to complete an asset return via
/// <see cref="IAssetReturnService.ReturnAsync(Guid,Guid,Guid?,AssetReturnOutcome,Guid,string?,CancellationToken)"/>.
/// </summary>
public enum AssetReturnResult
{
    /// <summary>The assignment was found, ownership verified (if requested), and closed.</summary>
    Success,

    /// <summary>No assignment exists for the given id/company.</summary>
    NotFound,

    /// <summary>The assignment has already been returned — a safe, idempotent no-op.</summary>
    AlreadyReturned,

    /// <summary>
    /// The assignment exists but belongs to a different employee than the caller expected — e.g. an
    /// offboarding asset-return task must never be able to close out an assignment belonging to
    /// someone other than the employee being offboarded. The assignment is left untouched.
    /// </summary>
    EmployeeMismatch
}

public interface IAssetReturnService
{
    /// <summary>
    /// Unverified return — used by Assets' own "Return asset" task flow, where the completing
    /// employee/HR user is already known to be acting on their own assignment via the Tasks module's
    /// existing authorization checks. Always records outcome <see cref="AssetReturnOutcome.Returned"/>.
    /// </summary>
    Task ReturnAsync(Guid companyId, Guid assignmentId, Guid returnedBy, CancellationToken cancellationToken);

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
    Task<AssetReturnResult> ReturnAsync(
        Guid companyId,
        Guid assignmentId,
        Guid? expectedEmployeeId,
        AssetReturnOutcome outcome,
        Guid returnedBy,
        string? notes,
        CancellationToken cancellationToken);
}
