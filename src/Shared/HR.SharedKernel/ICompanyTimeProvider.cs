namespace HR.SharedKernel;

public interface ICompanyTimeProvider
{
    DateOnly Today { get; }

    TimeZoneInfo TimeZone { get; }

    Task<DateOnly> GetTodayAsync(Guid companyId, CancellationToken cancellationToken = default);
}
