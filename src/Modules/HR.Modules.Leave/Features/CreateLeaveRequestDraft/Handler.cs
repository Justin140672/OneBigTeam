using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.CreateLeaveRequestDraft;

internal sealed class CreateLeaveRequestDraftHandler(
    LeaveDbContext dbContext,
    IClock clock,
    IWorkingPatternProvider workingPatternProvider,
    ICompanyLeaveSettingsReader leaveSettingsReader,
    IPublicHolidayReader publicHolidayReader)
{
    public async Task<Result<CreateLeaveRequestDraftResponse>> HandleAsync(
        CreateLeaveRequestDraftRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, CreateLeaveRequestDraftResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateLeaveRequestDraftResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var leaveType = await dbContext.LeaveTypes
            .SingleOrDefaultAsync(
                lt => lt.Id == request.LeaveTypeId && lt.CompanyId == request.CompanyId && lt.IsActive,
                cancellationToken);

        if (leaveType is null)
            return Result.Failure<CreateLeaveRequestDraftResponse>(
                Error.NotFound($"Leave type '{request.LeaveTypeId}' was not found."));

        var assignment = await dbContext.EmployeeLeavePolicyAssignments
            .SingleOrDefaultAsync(
                a => a.EmployeeId == request.EmployeeId && a.CompanyId == request.CompanyId,
                cancellationToken);

        var workingPattern = await workingPatternProvider.GetEffectivePatternAsync(
            request.CompanyId, request.EmployeeId, cancellationToken);

        var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(request.CompanyId, cancellationToken);

        List<DateOnly>? publicHolidayDates = null;
        if (leaveSettings.ExcludePublicHolidaysFromLeave)
        {
            var holidays = await publicHolidayReader.GetPublicHolidaysAsync(
                request.CompanyId, request.StartDate, request.EndDate, cancellationToken);
            publicHolidayDates = holidays.Select(h => h.Date).ToList();
        }

        var totalDays = LeaveCalculator.CalculateTotalDays(
            request.StartDate, request.StartPart, request.EndDate, request.EndPart, workingPattern, publicHolidayDates);

        var now = clock.UtcNowOffset();

        var draft = LeaveRequest.CreateDraft(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            request.LeaveTypeId,
            assignment?.LeavePolicyId,
            request.StartDate,
            request.StartPart,
            request.EndDate,
            request.EndPart,
            totalDays,
            request.Reason,
            now);

        dbContext.LeaveRequests.Add(draft);

        var response = new CreateLeaveRequestDraftResponse(
            draft.Id,
            draft.CompanyId,
            draft.EmployeeId,
            draft.LeaveTypeId,
            draft.LeavePolicyId,
            draft.Status.ToString(),
            draft.StartDate,
            draft.StartPart,
            draft.EndDate,
            draft.EndPart,
            draft.TotalDays,
            draft.Reason,
            draft.CreatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(dbContext.IdempotencyRecords,
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
