using System.Data;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Contracts;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class AssetNumberGenerator(CompaniesDbContext dbContext) : IAssetNumberGenerator
{
    public async Task<string> GenerateNextAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
            await connection.OpenAsync(cancellationToken);

        try
        {
            // Single atomic UPDATE ... RETURNING round-trip — see EmployeeNumberGenerator's own
            // remarks for the concurrency rationale, mirrored here for asset numbers.
            //
            // A missing row is not expected here: AssetNumberMode can only become Automatic via
            // UpdateAssetNumberSettings, which always persists a company_settings row as a side
            // effect — so by the time this mode is readable, the row already exists.
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE companies.company_settings
                SET next_asset_number = next_asset_number + 1
                WHERE company_id = @companyId
                RETURNING next_asset_number - 1, asset_number_prefix, asset_number_minimum_length
                """;

            var companyIdParameter = command.CreateParameter();
            companyIdParameter.ParameterName = "companyId";
            companyIdParameter.Value = companyId;
            command.Parameters.Add(companyIdParameter);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Cannot generate an asset number: company '{companyId}' has no company_settings row.");
            }

            var claimedNumber = reader.GetInt32(0);
            var prefix = reader.IsDBNull(1) ? null : reader.GetString(1);
            var minimumLength = reader.GetInt32(2);

            return $"{prefix}{claimedNumber.ToString().PadLeft(minimumLength, '0')}";
        }
        finally
        {
            if (wasClosed)
                await connection.CloseAsync();
        }
    }
}
