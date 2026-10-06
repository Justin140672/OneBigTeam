using HR.Modules.Recruitment.Services;

namespace HR.Modules.Recruitment.Features.GetInternalOffer;

internal sealed record GetInternalOfferResponse(
    Guid ApplicationId,
    Guid VacancyId,
    bool IsOfferRecipient,
    bool CanRespond,
    string? CannotRespondReason,
    OfferTermsView Terms,
    IReadOnlyList<string> InternalAppointmentNotices);
