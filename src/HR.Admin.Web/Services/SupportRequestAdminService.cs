using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

// Ticket 17: calls the platform-support API surface (/api/admin/companies/{companyId}/support/...,
// gated by "platform:admin" + the enabled Platform Administrator record check) rather than the
// tenant "support:manage" routes this service originally called. Those tenant routes required
// HR.Admin.Web's platform-administrator users to also hold a tenant HR role (support:manage),
// which the ticket explicitly forbids granting just to make these screens work — see
// UpdateSupportRequestStatus/AdminEndpoint.cs and ListSupportRequests/AdminEndpoint.cs, which reuse
// the exact same handlers as the tenant routes so query/transition/concurrency/notification
// behaviour is identical, only the authorization gate differs.
public sealed class SupportRequestAdminService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    /// <summary>
    /// Distinguishes unauthenticated (401), not-an-enabled-platform-administrator (403), the
    /// company not found (404), and a transient failure (network error / 5xx / anything else) —
    /// see SupportRequestFetchOutcome remarks. The page must show a different message for each.
    /// </summary>
    public async Task<SupportRequestListFetchResult> ListSupportRequestsAsync(
        Guid companyId, string? status = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"api/admin/companies/{companyId}/support/requests";
            if (!string.IsNullOrWhiteSpace(status))
                url += $"?status={Uri.EscapeDataString(status)}";

            var response = await Http.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var items = await response.Content.ReadFromJsonAsync<List<SupportRequestListItem>>(cancellationToken: cancellationToken);
                return new SupportRequestListFetchResult(SupportRequestFetchOutcome.Success, items ?? []);
            }

            return new SupportRequestListFetchResult(MapFailureOutcome(response.StatusCode), null);
        }
        catch (HttpRequestException)
        {
            return new SupportRequestListFetchResult(SupportRequestFetchOutcome.Failed, null);
        }
    }

    /// <summary>
    /// Distinguishes unauthenticated (401), not-an-enabled-platform-administrator (403), the
    /// request not found or belonging to a different company (404), and a transient failure — see
    /// SupportRequestFetchOutcome remarks.
    /// </summary>
    public async Task<SupportRequestDetailFetchResult> GetSupportRequestAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync($"api/admin/companies/{companyId}/support/requests/{id}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadFromJsonAsync<SupportRequestDetailModel>(cancellationToken: cancellationToken);
                return new SupportRequestDetailFetchResult(SupportRequestFetchOutcome.Success, detail);
            }

            return new SupportRequestDetailFetchResult(MapFailureOutcome(response.StatusCode), null);
        }
        catch (HttpRequestException)
        {
            return new SupportRequestDetailFetchResult(SupportRequestFetchOutcome.Failed, null);
        }
    }

    /// <summary>
    /// Ticket 15/17 optimistic-concurrency write path against the platform-support route.
    /// Distinguishes HTTP 409 (stale <paramref name="expectedVersion"/> — caller must show the
    /// conflict banner and not overwrite local state until the user explicitly reloads) from every
    /// other failure (network error, 403 not an enabled platform administrator, 404, 422
    /// validation, etc. — caller shows one generic error message). See
    /// UpdateSupportRequestStatus/AdminEndpoint.cs and ProblemResults.FromError for the exact
    /// `{ error, code }` 409 body shape this reads.
    /// </summary>
    public async Task<SupportRequestStatusUpdateResult> UpdateStatusAsync(
        Guid companyId, Guid id, string status, int? expectedVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PutAsJsonAsync(
                $"api/admin/companies/{companyId}/support/requests/{id}/status",
                new UpdateSupportRequestStatusRequest(companyId, id, status, expectedVersion),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<UpdateSupportRequestStatusResponse>(cancellationToken: cancellationToken);
                return new SupportRequestStatusUpdateResult(SupportRequestStatusUpdateOutcome.Success, result, null);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var errorMessage = await ReadErrorAsync(response, cancellationToken);
                return new SupportRequestStatusUpdateResult(SupportRequestStatusUpdateOutcome.Conflict, null, errorMessage);
            }

            return new SupportRequestStatusUpdateResult(
                SupportRequestStatusUpdateOutcome.Failed,
                null,
                await ReadErrorAsync(response, cancellationToken) ?? DescribeFailure(response.StatusCode));
        }
        catch (HttpRequestException ex)
        {
            return new SupportRequestStatusUpdateResult(SupportRequestStatusUpdateOutcome.Failed, null, ex.Message);
        }
    }

    private static SupportRequestFetchOutcome MapFailureOutcome(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => SupportRequestFetchOutcome.Unauthenticated,
        HttpStatusCode.Forbidden => SupportRequestFetchOutcome.NotAnEnabledPlatformAdministrator,
        HttpStatusCode.NotFound => SupportRequestFetchOutcome.NotFound,
        _ => SupportRequestFetchOutcome.Failed,
    };

    private static string DescribeFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "You are not signed in. Please sign in again.",
        HttpStatusCode.Forbidden => "You are not an enabled platform administrator, so you cannot perform this action.",
        HttpStatusCode.NotFound => "The support request could not be found.",
        _ => "A temporary error occurred. Please try again.",
    };

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (body.TryGetProperty("error", out var errorProp))
                return errorProp.GetString();
        }
        catch { }

        return null;
    }
}
