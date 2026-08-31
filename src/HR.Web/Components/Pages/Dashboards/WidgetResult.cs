namespace HR.Web.Components.Pages.Dashboards;

/// <summary>Load state of a single dashboard widget data source.</summary>
public enum WidgetLoadStatus
{
    Loading,
    Loaded,
    Failed,
}

/// <summary>
/// The outcome of loading one dashboard widget data source. Deliberately generic and
/// Blazor-agnostic so it can be unit-tested and reused by any widget. A <see cref="Failed"/>
/// result carries no value and must contribute nothing to the widget (no rows, no counts, no
/// "all clear") — see ticket DSH-03.
/// </summary>
public readonly record struct WidgetResult<T>(WidgetLoadStatus Status, T? Value, string SourceName)
{
    public static WidgetResult<T> Loading(string source) => new(WidgetLoadStatus.Loading, default, source);

    public static WidgetResult<T> Loaded(string source, T value) => new(WidgetLoadStatus.Loaded, value, source);

    public static WidgetResult<T> Failed(string source) => new(WidgetLoadStatus.Failed, default, source);

    public bool IsLoaded => Status == WidgetLoadStatus.Loaded;

    public bool IsFailed => Status == WidgetLoadStatus.Failed;

    public T ValueOrThrow => IsLoaded
        ? Value!
        : throw new InvalidOperationException($"Widget source '{SourceName}' is {Status}, not Loaded.");
}
