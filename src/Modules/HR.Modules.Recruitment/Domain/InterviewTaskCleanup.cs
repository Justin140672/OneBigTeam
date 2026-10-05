using System.Text.Json;

namespace HR.Modules.Recruitment.Domain;

internal sealed class InterviewTaskCleanup
{
    private InterviewTaskCleanup() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string InterviewIdsJson { get; private set; } = "[]";
    public int AttemptCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<Guid> InterviewIds =>
        JsonSerializer.Deserialize<List<Guid>>(InterviewIdsJson) ?? [];

    public static InterviewTaskCleanup Create(
        Guid id, Guid companyId, Guid applicationId, IReadOnlyCollection<Guid> interviewIds, DateTimeOffset now) =>
        new()
        {
            Id = id,
            CompanyId = companyId,
            ApplicationId = applicationId,
            InterviewIdsJson = JsonSerializer.Serialize(interviewIds),
            CreatedAt = now,
        };

    public void MarkCompleted(DateTimeOffset now)
    {
        AttemptCount++;
        LastAttemptAt = now;
        CompletedAt = now;
        FailureReason = null;
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        AttemptCount++;
        LastAttemptAt = now;
        FailureReason = reason.Length > 500 ? reason[..500] : reason;
    }
}
