using HR.Web.Models;

namespace HR.Web.Services;

public sealed class ProbationService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<ProbationRecordModel?> GetProbationRecordByEmployeeAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<ProbationRecordModel>(
                $"api/companies/{companyId}/employees/{employeeId}/probation-record", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<MyProbationStatusModel?> GetMyProbationStatusAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<MyProbationStatusModel>(
                $"api/companies/{companyId}/employees/me/probation-status", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<ProbationStatusModel?> GetStatusAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<ProbationStatusModel>(
                $"api/companies/{companyId}/employees/{employeeId}/probation-status", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<ProbationReviewDetailModel?> GetProbationReviewAsync(
        Guid companyId,
        Guid reviewId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<ProbationReviewDetailModel>(
                $"api/companies/{companyId}/probation-reviews/{reviewId}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> CompleteReviewAsync(
        Guid companyId,
        Guid probationRecordId,
        Guid reviewId,
        Guid completedByEmployeeId,
        string? notes,
        string? outcome = null,
        DateOnly? decisionDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/probation-records/{probationRecordId}/reviews/{reviewId}/complete",
                new { CompletedByEmployeeId = completedByEmployeeId, Notes = notes, Outcome = outcome, DecisionDate = decisionDate },
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<UpcomingProbationReviewItem>> GetUpcomingReviewsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetFromJsonAsync<UpcomingProbationReviewsResponse>(
                $"api/companies/{companyId}/probation-reviews/upcoming", HrApiJsonOptions.Default, cancellationToken);
            return response?.Items ?? [];
        }
        catch
        {
            return [];
        }
    }

    // DSH-03: non-swallowing sibling of GetUpcomingReviewsAsync.
    public async Task<IReadOnlyList<UpcomingProbationReviewItem>> GetUpcomingReviewsOrThrowAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        var response = await Http.GetFromJsonAsync<UpcomingProbationReviewsResponse>(
            $"api/companies/{companyId}/probation-reviews/upcoming", HrApiJsonOptions.Default, cancellationToken);
        return response?.Items ?? [];
    }

    /// <summary>
    /// Ticket 17: HR Administrator "administrative correction" edit — manager, expected end date,
    /// notes ONLY (status/extension/decision fields are workflow-owned and never sent here). See
    /// UpdateProbationRecordApiRequest and HR.Web.Components.Pages.EditSectionBase for the
    /// optimistic-concurrency contract: ExpectedVersion must be the Version last loaded, and the
    /// caller must distinguish HTTP 409 (stale — show the conflict banner, do not overwrite until
    /// the user explicitly reloads) from every other failure.
    /// </summary>
    public async Task<ApiSaveResult> UpdateProbationRecordAsync(
        Guid companyId, UpdateProbationRecordApiRequest request, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.PutAsJsonAsync(
                $"api/companies/{companyId}/probation-records/{request.ProbationRecordId}", request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return ApiSaveResult.Fail(ex.Message);
        }

        if (response.IsSuccessStatusCode)
        {
            var ok = await response.Content.ReadFromJsonAsync<UpdateProbationRecordApiResponse>(HrApiJsonOptions.Default, cancellationToken);
            return ApiSaveResult.Ok(ok?.Version);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Ticket 18: the endpoint now returns the original structured error code alongside the
            // message ({ error, code }, via ProblemResults.FromError), so a stale ExpectedVersion
            // ("concurrency") can be told apart from an ordinary business-rule rejection like a
            // terminal-status record ("conflict"). Only "concurrency" should drive the reload
            // banner; a malformed/empty/unknown body is treated as an ordinary failure, not a
            // concurrency conflict.
            var raw409 = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = TryDeserialize<ErrorEnvelope>(raw409);
            return ApiSaveResult.Fail(
                envelope?.Error ?? "Failed to save the probation record.",
                isConcurrencyConflict: envelope?.Code == "concurrency");
        }

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (TryDeserialize<ErrorEnvelope>(raw)?.Error is { } businessMessage)
            return ApiSaveResult.Fail(businessMessage);

        if (TryDeserialize<ValidationErrorResponse>(raw)?.Errors is { Count: > 0 } fieldErrors)
            return ApiSaveResult.Fail(string.Join(" ", fieldErrors.Values.SelectMany(m => m)));

        return ApiSaveResult.Fail($"Failed to save the probation record ({(int)response.StatusCode} {response.StatusCode}).");
    }

    private sealed record ErrorEnvelope(string? Error, string? Code);
    private sealed record ValidationErrorResponse(Dictionary<string, string[]>? Errors);

    private static T? TryDeserialize<T>(string json) where T : class
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<T>(json, HrApiJsonOptions.Default); }
        catch { return null; }
    }

    public async Task<IReadOnlyList<ProbationReviewModel>> GetProbationReviewsAsync(
        Guid companyId,
        Guid probationRecordId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetFromJsonAsync<ProbationReviewsResponse>(
                $"api/companies/{companyId}/probation-records/{probationRecordId}/reviews", HrApiJsonOptions.Default, cancellationToken);
            return response?.Items ?? [];
        }
        catch
        {
            return [];
        }
    }
}
