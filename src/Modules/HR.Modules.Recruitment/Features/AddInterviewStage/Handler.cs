using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.AddInterviewStage;

internal sealed class AddInterviewStageHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<AddInterviewStageResponse>> HandleAsync(
        AddInterviewStageRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var stages = await db.RecruitmentStages
            .Where(s => s.CompanyId == request.CompanyId)
            .ToListAsync(cancellationToken);

        var plan = InterviewStagePlanner.Plan(stages);

        var name = string.IsNullOrWhiteSpace(request.Name) ? plan.SuggestedName : request.Name.Trim();

        var renameTo = plan.StageToRename is null ? null : InterviewStagePlanner.OrdinalName(1);
        var reservedNames = stages
            .Select(s => s == plan.StageToRename && renameTo is not null ? renameTo : s.Name)
            .ToList();

        if (reservedNames.Any(n => string.Equals(n.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            return Result.Failure<AddInterviewStageResponse>(
                Error.Validation($"A recruitment stage named '{name}' already exists."));

        var now = clock.UtcNowOffset();
        var before = plan.StageToRename is null
            ? null
            : new RecruitmentStageAuditSnapshot(
                plan.StageToRename.Name, plan.StageToRename.IsTerminal, plan.StageToRename.TerminalOutcome, plan.StageToRename.Purpose);

        var parked = stages
            .Where(s => s.DisplayOrder >= plan.DisplayOrder)
            .Select(s => (Stage: s, Order: s.DisplayOrder))
            .ToList();

        if (parked.Count > 0)
        {
            foreach (var (stage, order) in parked)
                stage.SetDisplayOrder(-order, now);

            await db.SaveChangesAsync(cancellationToken);

            foreach (var (stage, order) in parked)
                stage.SetDisplayOrder(order + 1, now);
        }

        if (plan.StageToRename is { } renamed)
        {
            renamed.UpdateDetails(renameTo!, renamed.IsTerminal, renamed.TerminalOutcome, now, renamed.Purpose);
            renamed.IncrementVersion();
        }

        var created = RecruitmentStage.Create(
            Guid.NewGuid(),
            request.CompanyId,
            name,
            plan.DisplayOrder,
            false,
            RecruitmentStageTerminalOutcome.None,
            now,
            RecruitmentStagePurpose.Interview);

        db.RecruitmentStages.Add(created);

        await db.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new RecruitmentStageCreatedAuditEvent(
                created.CompanyId, created.Id, created.Name, created.DisplayOrder, created.IsTerminal, created.TerminalOutcome, now),
            cancellationToken);

        if (plan.StageToRename is { } r && before is not null)
        {
            await auditPublisher.PublishAsync(
                new RecruitmentStageUpdatedAuditEvent(
                    r.CompanyId, r.Id, before,
                    new RecruitmentStageAuditSnapshot(r.Name, r.IsTerminal, r.TerminalOutcome, r.Purpose), now),
                cancellationToken);
        }

        return Result.Success(new AddInterviewStageResponse(
            created.Id,
            created.CompanyId,
            created.Name,
            created.DisplayOrder,
            created.IsActive,
            plan.StageToRename?.Id,
            plan.StageToRename?.Name));
    }
}
