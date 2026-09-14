using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateDepartment;

internal sealed class CreateDepartmentHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;

    public CreateDepartmentHandler(EmployeesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<CreateDepartmentResponse>> HandleAsync(
        CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await _dbContext.TryReplayAsync<IdempotencyRecord, CreateDepartmentResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateDepartmentResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        if (request.ParentDepartmentId is not null)
        {
            var parentExists = await _dbContext.Departments
                .AnyAsync(
                    d => d.Id == request.ParentDepartmentId &&
                         d.CompanyId == request.CompanyId &&
                         d.IsActive,
                    cancellationToken);

            if (!parentExists)
            {
                return Result.Failure<CreateDepartmentResponse>(
                    Error.NotFound($"Parent department '{request.ParentDepartmentId}' was not found."));
            }
        }

        var nameExists = await _dbContext.Departments
            .AnyAsync(
                d => d.CompanyId == request.CompanyId &&
                     d.Name.ToLower() == request.Name.Trim().ToLower() &&
                     d.IsActive,
                cancellationToken);

        if (nameExists)
        {
            return Result.Failure<CreateDepartmentResponse>(
                Error.Conflict($"An active department named '{request.Name.Trim()}' already exists in this company."));
        }

        var now = _clock.UtcNowOffset();

        var department = Department.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.Name.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            now);

        if (request.ParentDepartmentId is not null)
        {
            department.Update(
                department.Name,
                department.Description,
                request.ParentDepartmentId,
                department.ManagerEmployeeId,
                now);
        }

        _dbContext.Departments.Add(department);

        var response = new CreateDepartmentResponse(
            department.Id,
            department.CompanyId,
            department.Name,
            department.Description,
            department.ParentDepartmentId,
            department.IsActive,
            department.CreatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await _dbContext.SaveIdempotentAsync<IdempotencyRecord, CreateDepartmentResponse>(_dbContext.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(response);
    }
}
