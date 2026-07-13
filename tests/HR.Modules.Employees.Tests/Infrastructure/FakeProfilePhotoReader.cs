using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Tests.Infrastructure;

// Simple in-memory stand-in for the Documents-module-backed IProfilePhotoReader, following the
// same "not found = absent" convention as the real implementation. Tests seed PhotoUrls directly
// rather than going through any storage/upload machinery.
internal sealed class FakeProfilePhotoReader : IProfilePhotoReader
{
    public Dictionary<Guid, string> PhotoUrls { get; } = new();

    public Task<IReadOnlyDictionary<Guid, string>> GetCurrentPhotoUrlsAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var ids = employeeIds.ToHashSet();
        var result = PhotoUrls
            .Where(kvp => ids.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return Task.FromResult<IReadOnlyDictionary<Guid, string>>(result);
    }
}
