using HR.SharedKernel;
using Serilog.Core;
using Serilog.Events;

namespace HR.Infrastructure.Logging;

/// <summary>
/// NFR-01: rewrites structured-log properties so sensitive values never reach a sink
/// (console, file, OTLP collector / traces).
///
/// For every log-event property:
/// <list type="bullet">
/// <item><description>if the property <b>name</b> is prohibited
/// (<see cref="SensitiveDataScrubber.IsProhibitedFieldName"/>) the whole value is replaced with
/// <see cref="SensitiveDataScrubber.Redacted"/>;</description></item>
/// <item><description>otherwise any sensitive-looking token inside a string value is scrubbed
/// via <see cref="SensitiveDataScrubber.ScrubText"/>.</description></item>
/// </list>
/// Serilog renders the output message from these same properties, so scrubbing them also
/// scrubs the rendered message.
/// </summary>
public sealed class SensitiveDataScrubbingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties.ToArray())
        {
            var scrubbed = ScrubValue(property.Key, property.Value);
            if (!ReferenceEquals(scrubbed, property.Value))
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, scrubbed));
        }
    }

    private static LogEventPropertyValue ScrubValue(string name, LogEventPropertyValue value)
    {
        var nameProhibited = SensitiveDataScrubber.IsProhibitedFieldName(name);

        switch (value)
        {
            case ScalarValue scalar:
                if (nameProhibited)
                    return new ScalarValue(SensitiveDataScrubber.Redacted);
                if (scalar.Value is string s)
                {
                    var scrubbed = SensitiveDataScrubber.ScrubText(s);
                    return ReferenceEquals(scrubbed, s) || scrubbed == s ? scalar : new ScalarValue(scrubbed);
                }
                return scalar;

            case StructureValue structure:
                return new StructureValue(
                    structure.Properties.Select(p =>
                        new LogEventProperty(p.Name, ScrubValue(p.Name, p.Value))),
                    structure.TypeTag);

            case DictionaryValue dictionary:
                return new DictionaryValue(dictionary.Elements.Select(kvp =>
                    new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                        kvp.Key,
                        ScrubValue(kvp.Key.Value?.ToString() ?? string.Empty, kvp.Value))));

            case SequenceValue sequence:
                return new SequenceValue(sequence.Elements.Select(e => ScrubValue(name, e)));

            default:
                return value;
        }
    }
}
