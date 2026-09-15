using HR.SharedKernel.Idempotency;

namespace HR.Web.Services;

// Common shape shared by every "simple" edit page's service (Department, EmploymentType, etc.):
// load one entity by id, create it, or update it. Lets EditPageBase<TModel, TKey> push the
// load/save orchestration that would otherwise be duplicated in every page's LoadAsync/SaveCoreAsync.
public interface IEditService<TModel, TKey>
{
    Task<TModel?> GetByIdAsync(Guid companyId, TKey id);
    Task<(TModel? Result, string? Error)> CreateAsync(Guid companyId, TModel model);
    Task<(TModel? Result, string? Error)> UpdateAsync(Guid companyId, TKey id, TModel model);
}

// Split out — not every entity supports deactivation (e.g. PublicHoliday has no delete today).
public interface IDeactivatableEditService<TModel, TKey> : IEditService<TModel, TKey>
{
    Task<string?> DeactivateAsync(Guid companyId, TKey id);
}

// Ticket 2 (optimistic concurrency): opt-in extension for a "simple" edit service whose update
// round-trips a version. EditPageBase<TModel, TKey> detects a service implementing this and plumbs
// the loaded version through: it is read from the model (IHasVersion) after GetByIdAsync, sent as
// the expected version on save, and the returned ApiSaveResult carries the new version plus the
// stale-save 409 ("concurrency") flag that raises the shared <SaveConflictBanner>.
public interface IConcurrencyAwareEditService<TModel, TKey> : IEditService<TModel, TKey>
{
    Task<ApiSaveResult> UpdateAsync(Guid companyId, TKey id, TModel model, int? expectedVersion);
}

// Ticket 3 (P1) final follow-up items 1/2: opt-in extension for a "simple" edit service whose
// CreateAsync is idempotency-key-aware. EditPageBase<TModel, TKey> detects a service implementing
// this and owns the key's lifecycle itself (generate once per logical create, reuse across retries
// of an unchanged submission, rotate the moment the submitted model changes, discard on a
// definitive outcome) - the service stays stateless with respect to operation identity.
// Ticket 3 (P1) final gap: returns a MutationOutcome<TModel> (not a plain tuple) so the caller
// (EditPageBase<TModel, TKey>) can tell a definitive outcome (Succeeded/Rejected — discard the
// idempotency key) apart from an AmbiguousFailure (5xx/408/429/transport/timeout/cancellation-after-
// dispatch/malformed success body — retain the key and let the user retry safely) without
// string-matching an error message or relying on a thrown exception as the only ambiguous signal.
public interface IIdempotentCreateService<TModel>
{
    Task<MutationOutcome<TModel>> CreateAsync(Guid companyId, TModel model, Guid idempotencyKey);

    // Bug fix (P1 follow-up to Ticket 19): EditPageBase<TModel, TKey> previously fingerprinted the
    // RAW edit model to build the idempotency key, before this service applied its own
    // FormText.Required/Optional normalization to build the outgoing request DTO. A whitespace-only
    // edit (or a blank vs. null difference on an optional field) therefore rotated the key even
    // though the actual HTTP request body was byte-for-byte identical - turning an ambiguous retry
    // (timeout/5xx/unconfirmed response) into a real duplicate-create risk.
    //
    // This hook lets the service expose the exact same canonical, normalized request object it is
    // about to send over HTTP, scoped to company/resource, so the caller (EditPageBase) can
    // fingerprint THAT instead of the raw model - without the service owning/storing the operation
    // key itself (that ownership stays on the page/dialog instance, per PendingIdempotentOperation).
    object BuildRequestSnapshot(Guid companyId, TModel model);
}

// Implemented by a "simple" edit model whose service round-trips an optimistic-concurrency token.
// EditPageBase<TModel, TKey> reads Version after loading (as the expected version for the next
// save) and writes the post-save version back onto it.
public interface IHasVersion
{
    int Version { get; set; }
}
