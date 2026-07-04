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
