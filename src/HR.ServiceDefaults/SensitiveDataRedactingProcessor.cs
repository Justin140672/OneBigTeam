using System.Diagnostics;
using HR.SharedKernel;
using OpenTelemetry;

namespace Microsoft.Extensions.Hosting;

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
                    data.SetTag(tag.Key, scrubbed);
                }
            }
        }
    }
}
