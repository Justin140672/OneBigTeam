using HR.Modules.Recruitment.Features.RecordInterviewOutcome;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal static class OutcomeWiring
{
    public static InterviewOutcomeAuditDelivery Delivery(RecruitmentDbContext db, FakeAuditPublisher audit) =>
        new(db, audit, audit, new FakeClock(DateTime.UtcNow));

    public static InterviewOutcomeRecorder Recorder(
        RecruitmentDbContext db, FakeAuditPublisher audit, ILogger<InterviewOutcomeRecorder>? logger = null) =>
        new(db, new FakeClock(DateTime.UtcNow), Delivery(db, audit), logger ?? NullLogger<InterviewOutcomeRecorder>.Instance);
}
