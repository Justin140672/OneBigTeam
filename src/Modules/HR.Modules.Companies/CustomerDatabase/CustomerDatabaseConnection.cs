using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.CustomerDatabase;

internal sealed class CustomerDatabaseConnection
{
    private readonly PlatformDbContext _platformDb;
    private readonly IConfiguration _config;
    private Memory<(Guid companyId, string? connectionString)> _cache;
    private DateTimeOffset _cacheExpiry;

    public CustomerDatabaseConnection(PlatformDbContext platformDb, IConfiguration config)
    {
        _platformDb = platformDb;
        _config = config;
        _cache = Memory<(Guid, string?)>.Empty;
        _cacheExpiry = DateTimeOffset.MinValue;
    }

    public async Task<string?> GetConnectionStringAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (_cache.Length > 0 && DateTimeOffset.UtcNow < _cacheExpiry)
        {
            var cachedEntry = _cache.Span[0];
            if (cachedEntry.companyId == companyId)
            {
                return cachedEntry.connectionString;
            }
        }

        var assignment = await _platformDb.CustomerDatabaseAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.CompanyId == companyId, cancellationToken);

        if (assignment is null || assignment.Status != CustomerDatabaseAssignmentStatus.Active)
        {
            throw new CustomerDatabaseUnavailableException(
                companyId,
                "No active database assignment found for this company.");
        }

        if (string.IsNullOrEmpty(assignment.DatabaseKey))
        {
            CacheResult(companyId, null);
            return null;
        }

        var configKey = $"CustomerDatabases:{assignment.DatabaseKey}:ConnectionString";
        var connectionString = _config[configKey];

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new CustomerDatabaseUnavailableException(
                companyId,
                "The assigned database connection is not configured.");
        }

        CacheResult(companyId, connectionString);
        return connectionString;
    }

    private void CacheResult(Guid companyId, string? connectionString)
    {
        _cache = new Memory<(Guid, string?)>(
            new[] { (companyId, connectionString) });
        _cacheExpiry = DateTimeOffset.UtcNow.AddSeconds(60);
    }
}
