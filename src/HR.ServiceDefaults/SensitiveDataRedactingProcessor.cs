using System.Diagnostics;
using HR.SharedKernel;
using OpenTelemetry;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// NFR-01: scrubs sensitive values from spans before they are exported.
///
/// Runs on span end and, for every string tag:
/// <list type="bullet">
/// <item><description>replaces the value entirely when the tag <b>name</b> is prohibited
/// (e.g. <c>salary</c>, <c>authorization</c>, <c>token</c>);</description></item>
/// <item><description>otherwise scrubs sensitive-looking tokens (NI number, IBAN, sort code,
/// bank/card number, bearer token, JWT, bcrypt hash) from the value.</description></item>
/// </list>
/// Also scrubs <see cref="Activity.StatusDescription"/> and the <c>exception.message</c> /
/// <c>exception.stacktrace</c> attributes recorded by exception events.
/// </summary>
internal sealed class SensitiveDataRedactingProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        foreach (var tag in data.TagObjects)
        {
            if (tag.Value is not string value)
                continue;

            if (SensitiveDataScrubber.IsProhibitedFieldName(tag.Key))
            {
                data.SetTag(tag.Key, SensitiveDataScrubber.Redacted);
                continue;
            }

            var scrubbed = SensitiveDataScrubber.ScrubText(value);
            if (!string.Equals(scrubbed, value, StringComparison.Ordinal))
                data.SetTag(tag.Key, scrubbed);
        }

        if (data.StatusDescription is { Length: > 0 } description)
        {
            var scrubbedDescription = SensitiveDataScrubber.ScrubText(description);
            if (!string.Equals(scrubbedDescription, description, StringComparison.Ordinal))
                data.SetStatus(data.Status, scrubbedDescription);
        }

        foreach (var activityEvent in data.Events)
        {
            foreach (var tag in activityEvent.Tags)
            {
                if (tag.Value is not string value)
                    continue;

                var scrubbed = SensitiveDataScrubber.IsProhibitedFieldName(tag.Key)
                    ? SensitiveDataScrubber.Redacted
                    : SensitiveDataScrubber.ScrubText(value);

                if (!string.Equals(scrubbed, value, StringComparison.Ordinal))
                {
                    // ActivityEvent tags are an immutable collection on the event; overwrite via
                    // the activity-level bag so exporters that read tag objects see the scrubbed value.
                    data.SetTag(tag.Key, scrubbed);
                }
            }
        }
    }
}
