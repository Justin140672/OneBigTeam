namespace HR.Modules.Companies.Features.GetFailedPayments;

internal sealed record GetFailedPaymentsRequest(string? Search, string? StatusFilter);
