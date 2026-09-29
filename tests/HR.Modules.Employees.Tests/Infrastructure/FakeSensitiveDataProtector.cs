using System.Text;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeSensitiveDataProtector : ISensitiveDataProtector
{
    public const string Prefix = "OBTENC1:test:";

    public string Protect(string plaintext)
        => Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

    public string Unprotect(string protectedValue)
    {
        if (!IsProtected(protectedValue))
            throw new SensitiveDataProtectionException("Value is not a recognised protected token.");

        return Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue[Prefix.Length..]));
    }

    public bool IsProtected(string? value)
        => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    public bool TryUnprotect(string? value, out string? plaintext)
    {
        if (IsProtected(value))
        {
            plaintext = Unprotect(value!);
            return true;
        }

        plaintext = null;
        return false;
    }
}
