using HR.Infrastructure.Abstractions;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeCompanyAcknowledgementSettingsReader(
    string statement = "I confirm that I have read and understood this document.") : ICompanyAcknowledgementSettingsReader
{
    public Task<string> GetDefaultAcknowledgementStatementAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(statement);
}
