using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace HR.Infrastructure.Abstractions;

public static class SupportSessionTokenConstants
{
    public const string Issuer = "hr-support-session";
    public const string Audience = "hr-support-session";
    public const string ConfigKey = "SupportSession:SigningKey";

    public static SecurityKey ResolveSigningKey(IConfiguration configuration)
    {
        var configured = configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Configuration value '{ConfigKey}' is required to issue or validate support-session tokens.");
        }

        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        return new SymmetricSecurityKey(keyBytes);
    }
}
