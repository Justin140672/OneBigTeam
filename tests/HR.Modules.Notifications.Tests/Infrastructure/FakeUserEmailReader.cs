using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Tests.Infrastructure;

/// <summary>
/// Mirrors HR.Modules.Support.Tests' FakeUserEmailReader (module-local copy, since test projects
/// don't share references to each other's Infrastructure folders).
/// </summary>
internal sealed class FakeUserEmailReader : IUserEmailReader
{
    private readonly string? _email;

    public FakeUserEmailReader(string? email = "recipient@example.test")
    {
        _email = email;
    }

    public Task<string?> GetEmailAsync(Guid companyId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_email);
}
