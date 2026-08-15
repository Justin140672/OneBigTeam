namespace HR.Modules.Companies.Features.GetPlatformSettings;

// FastEndpoints' RequestBinder<T> requires at least one publicly accessible property on the
// request DTO (a fully empty record/class throws a TypeInitializationException at first use) —
// this endpoint takes no real input (singleton row, no route/query parameters), so this property
// is unused by the handler and exists solely to satisfy that binder requirement.
internal sealed class GetPlatformSettingsRequest
{
    public bool Unused { get; init; }
}
