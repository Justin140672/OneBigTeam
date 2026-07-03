namespace HR.Infrastructure.Abstractions;

public interface IPublicHolidayReader
{
    Task<IReadOnlyCollection<PublicHolidayDate>> GetPublicHolidaysAsync(
        Guid companyId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}

public sealed record PublicHolidayDate(DateOnly Date, string Name);
