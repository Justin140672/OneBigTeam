using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.CompleteProbationReviewFromTask;

internal sealed class CompleteProbationReviewFromTaskAction(
    ProbationDbContext dbContext,
    IClock clock) : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.ProbationReview;

    public async Task ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null) return;

        var review = await dbContext.ProbationReviews
            .FirstOrDefaultAsync(
                r => r.Id == context.SourceEntityId && r.CompanyId == context.CompanyId,
                cancellationToken);

        if (review is null || review.Status == ProbationReviewStatus.Completed) return;

        var record = await dbContext.ProbationRecords
            .FirstOrDefaultAsync(
                r => r.Id == review.ProbationRecordId && r.CompanyId == context.CompanyId,
                cancellationToken);

        if (record is null) return;

        var now          = clock.UtcNowOffset();
        var decisionDate = DateOnly.FromDateTime(now.DateTime);

        ProbationOutcome? outcome = context.OutcomeDecision switch
        {
            "Pass"   => ProbationOutcome.Pass,
            "Fail"   => ProbationOutcome.Fail,
            "Extend" => ProbationOutcome.Extend,
            _        => null
        };

        if (outcome == ProbationOutcome.Pass)
            record.Pass(context.CompletedBy, decisionDate, context.OutcomeReason, now);
        else if (outcome == ProbationOutcome.Fail)
            record.Fail(context.CompletedBy, decisionDate, context.OutcomeReason, now);

        review.Complete(context.CompletedBy, context.OutcomeReason, now);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
