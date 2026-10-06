using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.RespondToInternalOffer;

internal sealed class RespondToInternalOfferHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    IEmployeeApplicantReader applicantReader,
    InternalOfferTaskEffectsService effectsService)
{
    private const int MaxAttempts = 3;

    private const string ConflictMessage = "This offer was changed at the same time. Reload and try again.";

    public async Task<Result<RespondToInternalOfferResponse>> HandleAsync(
        RespondToInternalOfferRequest request,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var target = string.Equals(request.Decision, "Accept", StringComparison.OrdinalIgnoreCase)
            ? OfferResponseStatus.Accepted
            : OfferResponseStatus.Declined;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var application = await db.Applications
                .SingleOrDefaultAsync(
                    a => a.Id == request.ApplicationId
                      && a.CompanyId == request.CompanyId
                      && a.Source == ApplicationSource.Internal,
                    cancellationToken);

            if (application is null || application.OfferResponseStatus is null)
                return NotFound();

            var linkedEmployeeId = await db.Candidates
                .AsNoTracking()
                .Where(c => c.Id == application.CandidateId && c.CompanyId == request.CompanyId)
                .Select(c => c.EmployeeId)
                .SingleOrDefaultAsync(cancellationToken);

            if (linkedEmployeeId != employeeId)
                return NotFound();

            if (request.OfferVersion != application.OfferVersion)
                return Result.Failure<RespondToInternalOfferResponse>(
                    Error.Conflict("This offer has been revised. Reload to review the latest terms."));

            if (application.OfferResponseStatus != OfferResponseStatus.AwaitingResponse)
            {
                if (application.OfferResponseStatus == target
                    && application.OfferRespondedByUserId == employeeId
                    && application.OfferResponseChannel == OfferResponseChannel.Employee)
                {
                    await effectsService.RunOutstandingForApplicationAsync(request.CompanyId, application.Id, cancellationToken);
                    return Result.Success(ToResponse(application, wasAlreadyRecorded: true));
                }

                return Result.Failure<RespondToInternalOfferResponse>(
                    Error.Conflict($"This offer has already been {application.OfferResponseStatus.ToString()!.ToLowerInvariant()}."));
            }

            if (application.WithdrawnAt is not null)
                return Result.Failure<RespondToInternalOfferResponse>(
                    Error.Validation("This application has been withdrawn."));

            if (application.AppointmentStatus is not null)
                return Result.Failure<RespondToInternalOfferResponse>(
                    Error.Conflict("This application is no longer open."));

            var isTerminal = await db.RecruitmentStages
                .AsNoTracking()
                .Where(s => s.Id == application.CurrentStageId && s.CompanyId == request.CompanyId)
                .Select(s => s.IsTerminal)
                .SingleOrDefaultAsync(cancellationToken);

            if (isTerminal)
                return Result.Failure<RespondToInternalOfferResponse>(
                    Error.Validation("This application is no longer open."));

            var employee = await applicantReader.GetApplicantAsync(request.CompanyId, employeeId, cancellationToken);
            if (employee is null || employee.EmploymentState != EmployeeApplicantEmploymentState.Active)
                return Result.Failure<RespondToInternalOfferResponse>(
                    Error.Validation("Only an active employee can respond to an internal offer."));

            var now = clock.UtcNowOffset();
            var expectedVersion = application.Version;
            var previousStatus = application.OfferResponseStatus.Value.ToString();

            application.RespondToOffer(
                target, now, employeeId, OfferResponseChannel.Employee,
                target == OfferResponseStatus.Declined ? request.Reason : null);

            var save = await db.SaveChangesWithConcurrencyAsync(
                application, expectedVersion, ConflictMessage, cancellationToken);

            if (save.IsFailure)
            {
                db.ChangeTracker.Clear();

                if (attempt < MaxAttempts)
                    continue;

                return Result.Failure<RespondToInternalOfferResponse>(save.Error);
            }

            await auditPublisher.PublishAsync(
                new OfferResponseRecordedAuditEvent(
                    application.CompanyId,
                    application.Id,
                    application.VacancyId,
                    application.CandidateId,
                    previousStatus,
                    target.ToString(),
                    employeeId,
                    now,
                    application.OfferVersion,
                    nameof(OfferResponseChannel.Employee)),
                cancellationToken);

            await effectsService.RunOutstandingForApplicationAsync(application.CompanyId, application.Id, cancellationToken);

            return Result.Success(ToResponse(application, wasAlreadyRecorded: false));
        }

        return Result.Failure<RespondToInternalOfferResponse>(Error.Concurrency(ConflictMessage));
    }

    private static Result<RespondToInternalOfferResponse> NotFound() =>
        Result.Failure<RespondToInternalOfferResponse>(Error.NotFound("Offer was not found."));

    private static RespondToInternalOfferResponse ToResponse(Domain.Application application, bool wasAlreadyRecorded) =>
        new(
            application.Id,
            application.VacancyId,
            application.OfferVersion,
            application.OfferResponseStatus!.Value.ToString(),
            application.OfferRespondedAt,
            wasAlreadyRecorded);
}
