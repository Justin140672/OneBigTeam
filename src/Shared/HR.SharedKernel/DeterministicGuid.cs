using System.Security.Cryptography;
using System.Text;

namespace HR.SharedKernel;

public static class DeterministicGuid
{
    public static Guid From(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        return new Guid(hash.AsSpan(0, 16));
    }
}
