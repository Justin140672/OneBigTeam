using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeCompanyAcknowledgementSettingsReader(
    string statement = "I confirm that I have read and understood this document.",
    int reminderIntervalDays = 3) : ICompanyAcknowledgementSettingsReader
{
    public Task<string> GetDefaultAcknowledgementStatementAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(statement);

    public Task<int> GetReminderIntervalDaysAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(reminderIntervalDays);
}
