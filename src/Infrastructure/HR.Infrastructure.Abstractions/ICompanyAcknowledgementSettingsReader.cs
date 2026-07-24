namespace HR.Infrastructure.Abstractions;

public interface ICompanyAcknowledgementSettingsReader
{
    Task<string> GetDefaultAcknowledgementStatementAsync(Guid companyId, CancellationToken cancellationToken);

    Task<int> GetReminderIntervalDaysAsync(Guid companyId, CancellationToken cancellationToken);
}
