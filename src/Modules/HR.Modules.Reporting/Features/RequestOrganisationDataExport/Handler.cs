using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Jobs;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.RequestOrganisationDataExport;

internal sealed class RequestOrganisationDataExportHandler(
    ReportingDbContext db,
    IOrganisationDataExportStatusReader statusReader,
    IBackgroundJobClient backgroundJobClient,
    IAuditEventPublisher auditEventPublisher,
    IClock clock)
{
    public async Task<Result<RequestOrganisationDataExportResponse>> HandleAsync(
        RequestOrganisationDataExportRequest request,
        Guid userId,
        string? requestedByDisplayName,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, RequestOrganisationDataExportResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<RequestOrganisationDataExportResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        if (await statusReader.HasActiveExportAsync(request.CompanyId, cancellationToken))
        {
            return Result.Failure<RequestOrganisationDataExportResponse>(Error.Conflict(
                "An organisation data export is already being prepared for this company. Wait for it to finish before requesting another."));
        }

        var now = clock.UtcNowOffset();
        var export = OrganisationDataExport.Create(request.CompanyId, userId, requestedByDisplayName, now);

        db.OrganisationDataExports.Add(export);

        var response = new RequestOrganisationDataExportResponse(export.Id, export.Status);

        try
        {
            if (request.IdempotencyKey is { } key)
            {
                var outcome = await db.SaveIdempotentAsync(
                    db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

                if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                {
                    return Result.Success(outcome.Response!);
                }
            }
            else
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbUpdateException)
        {
            // Ticket 3: lost a race with a concurrent request/retry on the
            // ix_organisation_data_exports_active_per_company filtered unique index.
            return Result.Failure<RequestOrganisationDataExportResponse>(Error.Conflict(
                "An organisation data export is already being prepared for this company. Wait for it to finish before requesting another."));
        }

        backgroundJobClient.Enqueue<OrganisationDataExportBuildJob>(
            job => job.RunAsync(export.Id, request.CompanyId, userId, CancellationToken.None));

        await auditEventPublisher.PublishAsync(
            new OrganisationDataExportRequestedAuditEvent(request.CompanyId, export.Id, userId, now),
            cancellationToken);

        return Result.Success(response);
    }
}
