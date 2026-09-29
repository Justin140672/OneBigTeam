using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Tests.Infrastructure;

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
