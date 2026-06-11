using HR.SharedKernel;

namespace HR.Integration.Tests.Infrastructure;

public sealed class FakeInviteLinkBuilder : IInviteLinkBuilder
{
    public string Build(string token) => $"https://test.local/invite/{token}";
}
