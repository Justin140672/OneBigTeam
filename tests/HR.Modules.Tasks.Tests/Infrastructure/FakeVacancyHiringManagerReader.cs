using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Tests.Infrastructure;

internal sealed class FakeVacancyHiringManagerReader(Dictionary<Guid, Guid>? hiringManagerIdsByInterviewId = null)
    : IVacancyHiringManagerReader
{
    private readonly Dictionary<Guid, Guid> _hiringManagerIdsByInterviewId =
        hiringManagerIdsByInterviewId ?? [];

    public Task<Guid?> GetHiringManagerIdForInterviewAsync(
        Guid companyId,
        Guid interviewId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_hiringManagerIdsByInterviewId.TryGetValue(interviewId, out var hiringManagerId)
            ? hiringManagerId
            : (Guid?)null);
}
