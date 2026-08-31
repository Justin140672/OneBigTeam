using HR.Web.Components.Pages.Dashboards;

namespace HR.Web.Services;

/// <summary>
/// Wraps a single dashboard widget data-source fetch so the calling widget can distinguish a
/// genuine empty result from a failure (ticket DSH-03). Exceptions are logged with correlation
/// context and never surfaced to the UI — the widget only ever sees Loaded / Failed.
/// </summary>
public sealed class WidgetSourceLoader(ILogger<WidgetSourceLoader> logger)
{
    public async Task<WidgetResult<T>> LoadAsync<T>(string widgetName, string sourceName, Func<Task<T>> fetch)
    {
        try
        {
            var value = await fetch();
            return WidgetResult<T>.Loaded(sourceName, value);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Dashboard widget source failed. Widget={Widget} Source={Source} CorrelationId={CorrelationId}",
                widgetName,
                sourceName,
                System.Diagnostics.Activity.Current?.Id ?? "none");
            return WidgetResult<T>.Failed(sourceName);
        }
    }
}
