namespace HR.Infrastructure.Abstractions;

public sealed class StagingSeedOptions
{
    public const string SectionName = "StagingSeed";

    public const string EmployeeNumberPrefix = "STG-";
    public const int EmployeeNumberMinimumLength = 3;
    public const int NextEmployeeNumber = 24;

    private const string DefaultCompanyName = "Staging Demo Ltd";
    private const string DefaultEmailDomain = "staging.example";
    private const int MinimumPasswordLength = 8;

    public static readonly Guid CompanyId = new("5A000000-0000-0000-0000-000000000001");

    public bool Enabled { get; set; }
    public string? CompanyName { get; set; }
    public string? EmailDomain { get; set; }
    public string? Password { get; set; }

    public string ResolvedCompanyName => Resolve(CompanyName, DefaultCompanyName);
    public string ResolvedEmailDomain => Resolve(EmailDomain, DefaultEmailDomain).TrimStart('@').ToLowerInvariant();

    public void ValidateOrThrow()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Password) || Password.Length < MinimumPasswordLength)
        {
            throw new InvalidOperationException(
                $"{SectionName}:Password must be provided (via secret/environment variable) with at least "
                + $"{MinimumPasswordLength} characters when {SectionName}:Enabled is true.");
        }
    }

    private static string Resolve(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
