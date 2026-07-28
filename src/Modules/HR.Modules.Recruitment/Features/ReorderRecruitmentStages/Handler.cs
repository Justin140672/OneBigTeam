using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ReorderRecruitmentStages;

internal sealed class ReorderRecruitmentStagesHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<ReorderRecruitmentStagesResponse>> HandleAsync(
        ReorderRecruitmentStagesRequest request,
        CancellationToken cancellationToken)
    {
        var stages = await db.RecruitmentStages
            .Where(s => s.CompanyId == request.CompanyId)
            .ToListAsync(cancellationToken);

        var stagesById = stages.ToDictionary(s => s.Id);

        // Every stage the company currently has must be present in the ordered list — partial
        // reorders are rejected rather than silently leaving some stages with a stale DisplayOrder.
        if (request.OrderedStageIds.Count != stages.Count ||
            request.OrderedStageIds.Any(id => !stagesById.ContainsKey(id)))
        {
            return Result.Failure<ReorderRecruitmentStagesResponse>(
                Error.Validation("The ordered stage id list must contain exactly this company's current recruitment stages, with no omissions or unknown ids."));
        }

        var now = clock.UtcNowOffset();

        // Two-phase update: the (company_id, display_order) unique index is checked immediately
        // (not deferred) by Postgres per-statement within the transaction, so directly reassigning
        // final 1..N values in one pass can collide with another row's current value mid-transaction
        // (e.g. swapping positions 1 and 2). First move every row to a unique negative placeholder,
        // then assign the real final values, each phase its own SaveChangesAsync/transaction.
        for (var i = 0; i < request.OrderedStageIds.Count; i++)
            stagesById[request.OrderedStageIds[i]].SetDisplayOrder(-(i + 1), now);

        await db.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < request.OrderedStageIds.Count; i++)
            stagesById[request.OrderedStageIds[i]].SetDisplayOrder(i + 1, now);

        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new RecruitmentStagesReorderedAuditEvent(request.CompanyId, request.OrderedStageIds, now),
            cancellationToken);

        var items = request.OrderedStageIds
            .Select(id => stagesById[id])
            .Select(s => new ReorderedStageItem(s.Id, s.Name, s.DisplayOrder))
            .ToList();

        return Result.Success(new ReorderRecruitmentStagesResponse(items));
    }
}
