using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.RejectLeaveRequest;

internal sealed class RejectLeaveRequestHandler(LeaveDbContext dbContext, INotificationWriter notificationWriter, IClock clock, IIntegrationEventPublisher publisher, ICompanyLeaveSettingsReader leaveSettingsReader, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<RejectLeaveRequestResponse>> HandleAsync(
        RejectLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, RejectLeaveRequestResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<RejectLeaveRequestResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var leaveRequest = await dbContext.LeaveRequests
            .SingleOrDefaultAsync(
                r => r.Id == request.LeaveRequestId
                  && r.EmployeeId == request.EmployeeId
                  && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (leaveRequest is null)
            return Result.Failure<RejectLeaveRequestResponse>(
                Error.NotFound($"Leave request '{request.LeaveRequestId}' was not found."));

        if (leaveRequest.Status is not (LeaveRequestStatus.Pending or LeaveRequestStatus.Approved))
            return Result.Failure<RejectLeaveRequestResponse>(
                Error.Validation($"Cannot reject a leave request with status '{leaveRequest.Status}'."));

        var now = clock.UtcNowOffset();

        if (leaveRequest.Status == LeaveRequestStatus.Approved)
        {
            var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(leaveRequest.CompanyId, cancellationToken);
            var policyYear = LeaveYearCalculator.GetPolicyYear(leaveRequest.StartDate, leaveSettings.LeaveYearStartMonth);

            var balance = await dbContext.LeaveBalances
                .SingleOrDefaultAsync(
                    b => b.EmployeeId == leaveRequest.EmployeeId
                      && b.CompanyId == leaveRequest.CompanyId
                      && b.LeaveTypeId == leaveRequest.LeaveTypeId
                      && b.PolicyYear == policyYear,
                    cancellationToken);

            balance?.ReverseUsage(leaveRequest.TotalDays, now);
        }

        leaveRequest.Reject(request.ReviewedByEmployeeId, now, request.RejectionReason);

        var response = new RejectLeaveRequestResponse(
            leaveRequest.Id,
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate,
            leaveRequest.StartPart,
            leaveRequest.EndDate,
            leaveRequest.EndPart,
            leaveRequest.TotalDays,
            leaveRequest.Status.ToString(),
            leaveRequest.ReviewedByEmployeeId!.Value,
            leaveRequest.ReviewedAt!.Value,
            leaveRequest.RejectionReason,
            leaveRequest.UpdatedAt);

        // P1 #4 (optimistic concurrency): see ApproveLeaveRequestHandler for why LeaveRequest and
        // LeaveBalance both carry a Version concurrency token, and why a stale save here must be
        // rejected rather than silently overwriting a concurrent approve/cancel/reject.
        try
        {
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
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<RejectLeaveRequestResponse>(
                Error.Concurrency("This leave request or its leave balance was changed by someone else. Reload and try again."));
        }

        var body = request.RejectionReason is not null
            ? $"Your leave from {leaveRequest.StartDate:d MMM yyyy} to {leaveRequest.EndDate:d MMM yyyy} has been rejected. Reason: {request.RejectionReason}"
            : $"Your leave from {leaveRequest.StartDate:d MMM yyyy} to {leaveRequest.EndDate:d MMM yyyy} has been rejected.";

        await notificationWriter.WriteAsync(
            Guid.NewGuid(), leaveRequest.CompanyId, leaveRequest.EmployeeId,
            "Your leave request has been rejected",
            body,
            leaveRequest.Id,
            NotificationType.LeaveRejected,
            NotificationPriority.Normal,
            now,
            cancellationToken);

        await auditPublisher.PublishAsync(new LeaveRejectedAuditEvent(
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.Id,
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate,
            leaveRequest.EndDate,
            leaveRequest.TotalDays,
            request.ReviewedByEmployeeId,
            request.RejectionReason,
            now), cancellationToken);

        await publisher.PublishAsync(new LeaveRejectedIntegrationEvent(
            leaveRequest.CompanyId,
            leaveRequest.EmployeeId,
            leaveRequest.Id,
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate,
            leaveRequest.EndDate,
            leaveRequest.TotalDays,
            request.ReviewedByEmployeeId,
            request.RejectionReason,
            now), cancellationToken);

        return Result.Success(response);
    }
}
