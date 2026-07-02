using System.Text.Json;
using System.Text.Json.Serialization;

namespace HR.Web.Services;

internal static class HrApiJsonOptions
{
    internal static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
