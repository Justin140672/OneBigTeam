using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="IRecruitmentPipelineSummaryReader"/> — records the arguments it
/// was called with and returns a pre-configured result.
/// </summary>
internal sealed class FakeRecruitmentPipelineSummaryReader : IRecruitmentPipelineSummaryReader
{
    private readonly RecruitmentPipelineSummaryResult _result;

    public FakeRecruitmentPipelineSummaryReader(RecruitmentPipelineSummaryResult? result = null)
    {
        _result = result ?? new RecruitmentPipelineSummaryResult([], []);
    }

    public Guid? LastCompanyId { get; private set; }
    public bool? LastIncludeClosed { get; private set; }

    public Task<RecruitmentPipelineSummaryResult> GetSummaryAsync(
        Guid companyId,
        bool includeClosed,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastIncludeClosed = includeClosed;

        return Task.FromResult(_result);
    }
}
