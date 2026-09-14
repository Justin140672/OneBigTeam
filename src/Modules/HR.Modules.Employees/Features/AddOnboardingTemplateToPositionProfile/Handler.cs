using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.AddOnboardingTemplateToPositionProfile;

internal sealed class AddOnboardingTemplateHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<AddOnboardingTemplateResponse>> HandleAsync(
        AddOnboardingTemplateRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, AddOnboardingTemplateResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<AddOnboardingTemplateResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var profileExists = await dbContext.PositionProfiles
            .AnyAsync(
                p => p.Id == request.PositionProfileId && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (!profileExists)
            return Result.Failure<AddOnboardingTemplateResponse>(
                Error.NotFound($"Position profile '{request.PositionProfileId}' was not found."));

        var templateExists = await dbContext.OnboardingTemplates
            .AnyAsync(
                t => t.Id == request.OnboardingTemplateId &&
                     t.CompanyId == request.CompanyId &&
                     t.IsActive,
                cancellationToken);

        if (!templateExists)
            return Result.Failure<AddOnboardingTemplateResponse>(
                Error.NotFound($"Onboarding template '{request.OnboardingTemplateId}' was not found."));

        var duplicateExists = await dbContext.PositionProfileOnboardingTemplates
            .AnyAsync(
                a => a.PositionProfileId == request.PositionProfileId &&
                     a.OnboardingTemplateId == request.OnboardingTemplateId &&
                     a.IsActive,
                cancellationToken);

        if (duplicateExists)
            return Result.Failure<AddOnboardingTemplateResponse>(
                Error.Conflict("This onboarding template is already assigned to the position profile."));

        var now = clock.UtcNowOffset();

        var assignment = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.PositionProfileId,
            request.OnboardingTemplateId,
            actorEmployeeId,
            now);

        dbContext.PositionProfileOnboardingTemplates.Add(assignment);

        var response = new AddOnboardingTemplateResponse(
            assignment.Id,
            assignment.PositionProfileId,
            assignment.OnboardingTemplateId);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync<IdempotencyRecord, AddOnboardingTemplateResponse>(dbContext.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new OnboardingTemplateAssignedAuditEvent(
                request.CompanyId,
                request.PositionProfileId,
                assignment.Id,
                request.OnboardingTemplateId,
                actorEmployeeId,
                now),
            cancellationToken);

        return Result.Success(response);
    }
}
