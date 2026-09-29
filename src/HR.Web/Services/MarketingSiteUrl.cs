using Microsoft.Extensions.Configuration;

namespace HR.Web.Services;

public static class MarketingSiteUrl
{
    public static string Resolve(IConfiguration configuration)
    {
        var baseUrl =
            configuration["services:marketing:https:0"] ??
            configuration["services:marketing:http:0"] ??
            "http://localhost:5166";

        return baseUrl.TrimEnd('/');
    }
}
