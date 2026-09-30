using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.SetDefaultOnboardingTemplate;

internal sealed class SetDefaultOnboardingTemplateHandler(EmployeesDbContext dbContext, IClock clock)
{
    public async Task<Result> HandleAsync(
        SetDefaultOnboardingTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, object?>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success();
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var template = await dbContext.OnboardingTemplates
            .SingleOrDefaultAsync(
                t => t.Id == request.Id && t.CompanyId == request.CompanyId,
                cancellationToken);

        if (template is null)
            return Result.Failure(Error.NotFound($"Onboarding template '{request.Id}' was not found."));

        if (!template.IsActive)
            return Result.Failure(Error.Validation("Cannot set an inactive onboarding template as the default."));

        if (template.IsDefault)
            return Result.Success();

        var now = clock.UtcNowOffset();

        var currentDefault = await dbContext.OnboardingTemplates
            .SingleOrDefaultAsync(
                t => t.CompanyId == request.CompanyId && t.IsDefault,
                cancellationToken);

        if (currentDefault is not null)
        {
            currentDefault.UnmarkAsDefault(now);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        template.MarkAsDefault(now);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync<IdempotencyRecord, object?>(dbContext.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status204NoContent, null, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success();
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
