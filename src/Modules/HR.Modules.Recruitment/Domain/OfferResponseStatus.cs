namespace HR.Modules.Recruitment.Domain;

// Ticket 2: explicit lifecycle of the offer made to a candidate for this application. Set to
// AwaitingResponse the moment offer terms are recorded (OfferCandidate); moved to a terminal value
// via RespondToOffer. Null means no offer has been made yet. Distinct from Application.WithdrawnAt
// (candidate withdrawing from the whole process) — Withdrawn here means the employer pulled the offer.
internal enum OfferResponseStatus
{
    AwaitingResponse,
    Accepted,
    Declined,
    Withdrawn
}
