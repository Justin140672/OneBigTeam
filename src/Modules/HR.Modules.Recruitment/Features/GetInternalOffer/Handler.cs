using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.GetInternalOffer;

internal sealed class GetInternalOfferHandler(RecruitmentDbContext db, IAuthorizationService authorizationService)
{
    public static readonly IReadOnlyList<string> InternalAppointmentNotices =
    [
        "This is an internal appointment. No new employee record is created and your employee number does not change.",
        "Your original start date and continuous-service date are unchanged.",
        "Your existing system access is unchanged and no new-starter onboarding is triggered.",
    ];

    public async Task<Result<GetInternalOfferResponse>> HandleAsync(
        GetInternalOfferRequest request,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        var row = await (
            from a in db.Applications.AsNoTracking()
            join c in db.Candidates.AsNoTracking() on a.CandidateId equals c.Id
            join s in db.RecruitmentStages.AsNoTracking() on a.CurrentStageId equals s.Id
            where a.Id == request.ApplicationId
               && a.CompanyId == request.CompanyId
               && a.Source == ApplicationSource.Internal
               && a.OfferResponseStatus != null
            select new { Application = a, c.EmployeeId, s.IsTerminal })
            .SingleOrDefaultAsync(cancellationToken);

        var notFound = Result.Failure<GetInternalOfferResponse>(Error.NotFound("Offer was not found."));

        if (row is null)
            return notFound;

        var isRecipient = row.EmployeeId == callerId;

        if (!isRecipient &&
            !await authorizationService.HasPermissionAsync(callerId, SystemPermissions.RecruitmentManage, cancellationToken))
            return notFound;

        var application = row.Application;
        var reason = isRecipient
            ? DescribeCannotRespond(application, row.IsTerminal)
            : "Only the employee receiving the offer can respond.";

        return Result.Success(new GetInternalOfferResponse(
            application.Id,
            application.VacancyId,
            isRecipient,
            isRecipient && reason is null,
            reason,
            OfferTermsView.From(application),
            InternalAppointmentNotices));
    }

    internal static string? DescribeCannotRespond(Application application, bool isTerminalStage)
    {
        if (application.WithdrawnAt is not null)
            return "This application has been withdrawn.";

        if (application.OfferResponseStatus != OfferResponseStatus.AwaitingResponse)
            return $"This offer has already been {application.OfferResponseStatus.ToString()!.ToLowerInvariant()}.";

        if (isTerminalStage || application.AppointmentStatus is not null)
            return "This application is no longer open.";

        return null;
    }
}
