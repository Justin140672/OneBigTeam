using HR.Infrastructure.Abstractions;

namespace HR.Modules.Onboarding.Tests.Infrastructure;

internal sealed class FakeProbationSummaryReader(ProbationSummaryItem? item = null) : IProbationSummaryReader
{
    public Task<ProbationSummaryItem?> GetSummaryAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(item);
}
