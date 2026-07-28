using HR.SharedKernel;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeInviteLinkBuilder : IInviteLinkBuilder
{
    public string Build(string token) => $"https://test.local/invite/{token}";
}
