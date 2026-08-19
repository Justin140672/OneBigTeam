namespace HR.Modules.Companies.Contracts;

public interface ICompanyAcknowledgementSettingsReader
{
    Task<string> GetDefaultAcknowledgementStatementAsync(Guid companyId, CancellationToken cancellationToken);

    Task<int> GetReminderIntervalDaysAsync(Guid companyId, CancellationToken cancellationToken);
}
