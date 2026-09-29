using HR.SharedKernel;
using Microsoft.Extensions.Configuration;

namespace HR.Infrastructure.Email;

internal sealed class ConfiguredInviteLinkBuilder(IConfiguration configuration) : IInviteLinkBuilder
{
    private const string FallbackBaseUrl = "http://localhost:5157";

    public string Build(string token)
    {
        var baseUrl = configuration["WebApp:BaseUrl"]?.TrimEnd('/') ?? FallbackBaseUrl;
        return $"{baseUrl}/invite/{Uri.EscapeDataString(token)}";
    }
}
