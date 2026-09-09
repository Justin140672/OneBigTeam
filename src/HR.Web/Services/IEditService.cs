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

// Implemented by a "simple" edit model whose service round-trips an optimistic-concurrency token.
// EditPageBase<TModel, TKey> reads Version after loading (as the expected version for the next
// save) and writes the post-save version back onto it.
public interface IHasVersion
{
    int Version { get; set; }
}
