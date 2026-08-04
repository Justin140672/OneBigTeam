using HR.Infrastructure.Abstractions;

namespace HR.Modules.Support.Tests.Infrastructure;

internal sealed class FakeUserEmailReader : IUserEmailReader
{
    private readonly string? _email;

    public FakeUserEmailReader(string? email = "submitter@example.test")
    {
        _email = email;
    }

    public Task<string?> GetEmailAsync(Guid companyId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_email);
}
