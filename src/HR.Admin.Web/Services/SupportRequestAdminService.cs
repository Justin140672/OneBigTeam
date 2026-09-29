using HR.Admin.Web.Models;
using HR.SharedKernel.Http;

namespace HR.Admin.Web.Services;

// Ticket 17: calls the platform-support API surface (/api/admin/companies/{companyId}/support/...,
// gated by "platform:admin" + the enabled Platform Administrator record check) rather than the
// tenant "support:manage" routes this service originally called. Those tenant routes required
// HR.Admin.Web's platform-administrator users to also hold a tenant HR role (support:manage),
// which the ticket explicitly forbids granting just to make these screens work — see
// UpdateSupportRequestStatus/AdminEndpoint.cs and ListSupportRequests/AdminEndpoint.cs, which reuse
// the exact same handlers as the tenant routes so query/transition/concurrency/notification
// behaviour is identical, only the authorization gate differs.
//
// Ticket 3 (P1): responses are read through the shared ApiResponseReader, so network failures, 5xx
// responses and malformed/empty bodies surface as SupportRequestFetchOutcome.Failed (never as an empty
// or "not found" result), and caller cancellation propagates instead of being swallowed.
public sealed class SupportRequestAdminService(
    HrApiHttpClientFactory httpClientFactory,
    ILogger<SupportRequestAdminService>? logger = null)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<SupportRequestListFetchResult> ListSupportRequestsAsync(
        Guid companyId, string? status = null, CancellationToken cancellationToken = default)
    {
        var url = $"api/admin/companies/{companyId}/support/requests";
        if (!string.IsNullOrWhiteSpace(status))
            url += $"?status={Uri.EscapeDataString(status)}";

        var result = await ApiResponseReader.ExecuteAsync<List<SupportRequestListItem>>(
            ct => Http.GetAsync(url, ct), cancellationToken: cancellationToken);
        result.LogFailure(logger, "SupportRequests.List");

        // A 200 with an empty JSON array is a genuine empty list; a 200 with a null body is malformed.
        if (result.Success)
        {
            return result.Value is null
                ? new SupportRequestListFetchResult(SupportRequestFetchOutcome.Failed, null)
                : new SupportRequestListFetchResult(SupportRequestFetchOutcome.Success, result.Value);
        }

        return new SupportRequestListFetchResult(MapFailureOutcome(result.FailureKind), null);
    }

    public async Task<SupportRequestDetailFetchResult> GetSupportRequestAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<SupportRequestDetailModel>(
            ct => Http.GetAsync($"api/admin/companies/{companyId}/support/requests/{id}", ct),
            cancellationToken: cancellationToken);
        result.LogFailure(logger, "SupportRequests.Get");

        if (result.Success)
        {
            return result.Value is null
                ? new SupportRequestDetailFetchResult(SupportRequestFetchOutcome.Failed, null)
                : new SupportRequestDetailFetchResult(SupportRequestFetchOutcome.Success, result.Value);
        }

        return new SupportRequestDetailFetchResult(MapFailureOutcome(result.FailureKind), null);
    }

    /// <summary>
    /// Ticket 15/17 optimistic-concurrency write path against the platform-support route.
    /// Distinguishes HTTP 409 (stale <paramref name="expectedVersion"/> — caller must show the
    /// conflict banner and not overwrite local state until the user explicitly reloads) from every
    /// other failure (network error, 403 not an enabled platform administrator, 404, 422
    /// validation, etc. — caller shows one safe error message). A failed call never reports Success.
    /// See UpdateSupportRequestStatus/AdminEndpoint.cs and ProblemResults.FromError for the exact
    /// `{ error, code }` 409 body shape this reads.
    /// </summary>
    public async Task<SupportRequestStatusUpdateResult> UpdateStatusAsync(
        Guid companyId, Guid id, string status, int? expectedVersion, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<UpdateSupportRequestStatusResponse>(
            ct => Http.PutAsJsonAsync(
                $"api/admin/companies/{companyId}/support/requests/{id}/status",
                new UpdateSupportRequestStatusRequest(companyId, id, status, expectedVersion),
                ct),
            cancellationToken: cancellationToken);
        result.LogFailure(logger, "SupportRequests.UpdateStatus");

        if (result.Success)
        {
            return result.Value is null
                ? new SupportRequestStatusUpdateResult(
                    SupportRequestStatusUpdateOutcome.Failed, null, DescribeFailure(ApiFailureKind.InvalidResponse, null))
                : new SupportRequestStatusUpdateResult(SupportRequestStatusUpdateOutcome.Success, result.Value, null);
        }

        // 409 in either flavour (explicit concurrency code or a plain conflict) is the stale-version case.
        if (result.FailureKind is ApiFailureKind.Concurrency or ApiFailureKind.Conflict)
        {
            return new SupportRequestStatusUpdateResult(
                SupportRequestStatusUpdateOutcome.Conflict, null, result.DisplayMessage);
        }

        return new SupportRequestStatusUpdateResult(
            SupportRequestStatusUpdateOutcome.Failed, null, DescribeFailure(result.FailureKind, result.DisplayMessage));
    }

    private static SupportRequestFetchOutcome MapFailureOutcome(ApiFailureKind kind) => kind switch
    {
        ApiFailureKind.Unauthenticated => SupportRequestFetchOutcome.Unauthenticated,
        ApiFailureKind.Forbidden => SupportRequestFetchOutcome.NotAnEnabledPlatformAdministrator,
        ApiFailureKind.NotFound => SupportRequestFetchOutcome.NotFound,
        _ => SupportRequestFetchOutcome.Failed,
    };

    // Only API-authored validation text is passed through; every other kind gets a fixed, safe message
    // (never raw exception or response-body text).
    private static string DescribeFailure(ApiFailureKind kind, string? apiMessage) => kind switch
    {
        ApiFailureKind.Unauthenticated => "You are not signed in. Please sign in again.",
        ApiFailureKind.Forbidden => "You are not an enabled platform administrator, so you cannot perform this action.",
        ApiFailureKind.NotFound => "The support request could not be found.",
        ApiFailureKind.Validation when !string.IsNullOrWhiteSpace(apiMessage) => apiMessage,
        _ => "A temporary error occurred. Please try again.",
    };
}
