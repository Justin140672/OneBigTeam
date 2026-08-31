using HR.Infrastructure.Logging;
using HR.SharedKernel;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HR.Infrastructure.Tests;

/// <summary>
/// NFR-01: the Serilog enricher must scrub sensitive log-event properties before any sink sees
/// them — both by property name (whole value redacted) and by value pattern (token scrubbed).
/// </summary>
public class SensitiveDataScrubbingEnricherTests
{
    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static (ILogger Logger, CapturingSink Sink) BuildLogger()
    {
        var sink = new CapturingSink();
        var logger = new LoggerConfiguration()
            .Enrich.With(new SensitiveDataScrubbingEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();
        return (logger, sink);
    }

    private static string ScalarText(LogEventPropertyValue value)
        => Assert.IsType<ScalarValue>(value).Value?.ToString() ?? string.Empty;

    [Fact]
    public void Redacts_whole_value_when_property_name_is_prohibited()
    {
        var (logger, sink) = BuildLogger();

        logger.ForContext("password", "hunter2").Information("login attempt");

        var evt = Assert.Single(sink.Events);
        Assert.Equal(SensitiveDataScrubber.Redacted, ScalarText(evt.Properties["password"]));
    }

    [Fact]
    public void Scrubs_sensitive_token_inside_an_innocuous_property()
    {
        var (logger, sink) = BuildLogger();

        logger.ForContext("note", "NI AB123456C").Information("note added");

        var evt = Assert.Single(sink.Events);
        var text = ScalarText(evt.Properties["note"]);
        Assert.Contains(SensitiveDataScrubber.Redacted, text);
        Assert.DoesNotContain("AB123456C", text);
    }

    [Fact]
    public void Scrubs_rendered_message_via_message_template_property()
    {
        var (logger, sink) = BuildLogger();

        logger.Information("Authorization {Auth}", "Bearer xyz.abc-123");

        var evt = Assert.Single(sink.Events);
        var rendered = evt.RenderMessage();
        Assert.Contains(SensitiveDataScrubber.Redacted, rendered);
        Assert.DoesNotContain("xyz.abc-123", rendered);
    }

    [Fact]
    public void Leaves_clean_properties_untouched()
    {
        var (logger, sink) = BuildLogger();

        logger.ForContext("currency", "GBP").ForContext("direction", "Increase").Information("ok");

        var evt = Assert.Single(sink.Events);
        Assert.Equal("GBP", ScalarText(evt.Properties["currency"]));
        Assert.Equal("Increase", ScalarText(evt.Properties["direction"]));
    }
}
