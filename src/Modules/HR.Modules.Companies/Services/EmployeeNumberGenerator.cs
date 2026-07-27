using System.Data;
using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class EmployeeNumberGenerator(CompaniesDbContext dbContext) : IEmployeeNumberGenerator
{
    public async Task<string> GenerateNextAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
            await connection.OpenAsync(cancellationToken);

        try
        {
            // Single atomic UPDATE ... RETURNING round-trip: Postgres's row-level lock on the
            // company_settings row during the UPDATE naturally serializes concurrent callers for
            // the same company_id, so two concurrent calls each get a distinct number with no
            // application-level read-then-write race and no retry loop required. Prefix and
            // minimum-length are fetched in the same statement to avoid a second round-trip.
            //
            // A missing row is not expected here: EmployeeNumberMode can only become Automatic via
            // UpdateCompanySettings, which always persists a company_settings row as a side
            // effect — so by the time this mode is readable, the row already exists.
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE companies.company_settings
                SET next_employee_number = next_employee_number + 1
                WHERE company_id = @companyId
                RETURNING next_employee_number - 1, employee_number_prefix, employee_number_minimum_length
                """;

            var companyIdParameter = command.CreateParameter();
            companyIdParameter.ParameterName = "companyId";
            companyIdParameter.Value = companyId;
            command.Parameters.Add(companyIdParameter);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Cannot generate an employee number: company '{companyId}' has no company_settings row.");
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
