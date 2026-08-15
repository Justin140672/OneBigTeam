namespace HR.Modules.Companies.Features.GetFailedPayments;

/// <summary>
/// StatusFilter is one of Stripe's own invoice statuses for a failed payment ("open" or
/// "uncollectible"), or null/omitted for "all failed payments" — mirrors the explicit-value
/// filtering convention already used elsewhere in the Admin Portal rather than a free-text status.
/// </summary>
internal sealed record GetFailedPaymentsRequest(string? Search, string? StatusFilter);
