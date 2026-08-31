using System.Diagnostics;
using HR.SharedKernel;
using Microsoft.Extensions.Hosting;

namespace HR.Architecture.Tests;

/// <summary>
/// NFR-01: the OpenTelemetry span processor must strip sensitive values from span tags before
/// they are exported — redacting whole values for prohibited tag names and scrubbing
/// sensitive-looking tokens from other tag values.
/// </summary>
public class SensitiveDataRedactingProcessorTests
{
    private static Activity StartActivity()
    {
        var source = new ActivitySource("HR.Architecture.Tests.Nfr01");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var activity = source.StartActivity("work", ActivityKind.Internal);
        Assert.NotNull(activity);
        return activity!;
    }

    [Fact]
    public void OnEnd_redacts_prohibited_tag_and_scrubs_sensitive_values()
    {
        var processor = new SensitiveDataRedactingProcessor();

        using var activity = StartActivity();
        activity.SetTag("authorization", "Bearer abc.def-ghi");
        activity.SetTag("http.url", "https://x/y?nino=AB123456C");
        activity.SetTag("safe", "ok");
        activity.Stop();

        processor.OnEnd(activity);

        Assert.Equal(SensitiveDataScrubber.Redacted, activity.GetTagItem("authorization"));

        var url = Assert.IsType<string>(activity.GetTagItem("http.url"));
        Assert.Contains(SensitiveDataScrubber.Redacted, url);
        Assert.DoesNotContain("AB123456C", url);

        Assert.Equal("ok", activity.GetTagItem("safe"));
    }
}
