using HR.SharedKernel;
using Serilog.Core;
using Serilog.Events;

namespace HR.Infrastructure.Logging;

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
