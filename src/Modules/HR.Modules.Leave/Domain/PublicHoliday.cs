namespace HR.Modules.Leave.Domain;

internal sealed class PublicHoliday
{
    private PublicHoliday() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public DateOnly Date { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string CountryCode { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static PublicHoliday Create(
        Guid id,
        Guid companyId,
        DateOnly date,
        string name,
        string countryCode,
        DateTimeOffset now)
    {
        return new PublicHoliday
        {
            Id = id,
            CompanyId = companyId,
            Date = date,
            Name = name,
            CountryCode = countryCode,
            CreatedAt = now
        };
    }
}
