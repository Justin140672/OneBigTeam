namespace HR.Modules.Companies.Contracts;

public sealed record EmployeeNumberSequencePreview(string? Prefix, int NextNumber, int MinimumLength);

public interface ICompanyEmployeeNumberSettingsReader
{
    Task<EmployeeNumberMode> GetModeAsync(Guid companyId, CancellationToken cancellationToken);

    // Read-only snapshot of the sequence (prefix, next number, minimum length). Callers must not
    // mutate any state from this call; only IEmployeeNumberGenerator.GenerateNextAsync claims a
    // number and advances the counter.
    Task<EmployeeNumberSequencePreview> GetSequencePreviewAsync(Guid companyId, CancellationToken cancellationToken);
}
