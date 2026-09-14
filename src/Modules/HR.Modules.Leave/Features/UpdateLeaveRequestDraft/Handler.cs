using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.UpdateLeaveRequestDraft;

// LEAVE-07: only a Draft owned by the caller can be edited-as-draft. Submitted, pending, approved,
// rejected and cancelled requests are guarded off both here and defensively inside
// LeaveRequest.UpdateDraftDetails - see LeaveRequestStatus.Draft doc comment.
internal sealed class UpdateLeaveRequestDraftHandler(
    LeaveDbContext dbContext,
    IClock clock,
    IWorkingPatternProvider workingPatternProvider,
    ICompanyLeaveSettingsReader leaveSettingsReader,
    IPublicHolidayReader publicHolidayReader)
{
    public async Task<Result<UpdateLeaveRequestDraftResponse>> HandleAsync(
        UpdateLeaveRequestDraftRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, UpdateLeaveRequestDraftResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<UpdateLeaveRequestDraftResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var draft = await dbContext.LeaveRequests
            .SingleOrDefaultAsync(
                r => r.Id == request.LeaveRequestId
                  && r.EmployeeId == request.EmployeeId
                  && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (draft is null)
            return Result.Failure<UpdateLeaveRequestDraftResponse>(
                Error.NotFound($"Leave request '{request.LeaveRequestId}' was not found."));

        if (draft.Status != LeaveRequestStatus.Draft)
            return Result.Failure<UpdateLeaveRequestDraftResponse>(
                Error.Validation($"Cannot edit a leave request with status '{draft.Status}' as a draft."));

        var leaveType = await dbContext.LeaveTypes
            .SingleOrDefaultAsync(
                lt => lt.Id == request.LeaveTypeId && lt.CompanyId == request.CompanyId && lt.IsActive,
                cancellationToken);

        if (leaveType is null)
            return Result.Failure<UpdateLeaveRequestDraftResponse>(
                Error.NotFound($"Leave type '{request.LeaveTypeId}' was not found."));

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

        draft.UpdateDraftDetails(
            request.LeaveTypeId,
            request.StartDate,
            request.StartPart,
            request.EndDate,
            request.EndPart,
            totalDays,
            request.Reason,
            now);

        var response = new UpdateLeaveRequestDraftResponse(
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
            draft.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(dbContext.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

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
