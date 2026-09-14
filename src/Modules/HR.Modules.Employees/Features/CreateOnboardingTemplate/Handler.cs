using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateOnboardingTemplate;

internal sealed class CreateOnboardingTemplateHandler(EmployeesDbContext dbContext, IClock clock)
{
    public async Task<Result<CreateOnboardingTemplateResponse>> HandleAsync(
        CreateOnboardingTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, CreateOnboardingTemplateResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateOnboardingTemplateResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var newName = request.Name.Trim();

        var nameExists = await dbContext.OnboardingTemplates
            .AnyAsync(
                t => t.CompanyId == request.CompanyId &&
                     t.Name == newName &&
                     t.IsActive,
                cancellationToken);

        if (nameExists)
        {
            return Result.Failure<CreateOnboardingTemplateResponse>(
                Error.Conflict($"An active onboarding template named '{newName}' already exists in this company."));
        }

        var now = clock.UtcNowOffset();

        var template = OnboardingTemplate.Create(
            Guid.NewGuid(),
            request.CompanyId,
            newName,
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            now);

        dbContext.OnboardingTemplates.Add(template);

        var response = new CreateOnboardingTemplateResponse(
            template.Id,
            template.CompanyId,
            template.Name,
            template.Description,
            template.IsActive,
            template.CreatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync<IdempotencyRecord, CreateOnboardingTemplateResponse>(dbContext.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(response);
    }
}
