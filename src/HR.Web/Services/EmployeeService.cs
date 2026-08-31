using HR.Web.Models;
using System.Web;

namespace HR.Web.Services;

public class EmployeeService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListEmployeesResponse?> ListEmployeesAsync(
        Guid companyId,
        string? search = null,
        int pageNumber = 1,
        int pageSize = 20,
        Guid? departmentId = null,
        string? status = null,
        Guid? managerId = null,
        Guid? locationId = null)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;
        query["pageNumber"] = pageNumber.ToString();
        query["pageSize"] = pageSize.ToString();
        if (departmentId is not null) query["departmentId"] = departmentId.ToString();
        if (!string.IsNullOrWhiteSpace(status)) query["status"] = status;
        if (managerId is not null) query["managerId"] = managerId.ToString();
        if (locationId is not null) query["locationId"] = locationId.ToString();

        try
        {
            return await Http.GetFromJsonAsync<ListEmployeesResponse>(
                $"api/companies/{companyId}/employees?{query}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetHeadcountSummaryResponse?> GetHeadcountSummaryAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetHeadcountSummaryResponse>(
                $"api/companies/{companyId}/employees/headcount-summary", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<GetGenderSplitResponse?> GetGenderSplitAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetGenderSplitResponse>(
                $"api/companies/{companyId}/employees/gender-split", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<GetEmploymentTypeSplitResponse?> GetEmploymentTypeSplitAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetEmploymentTypeSplitResponse>(
                $"api/companies/{companyId}/employees/employment-type-split", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<GetNewHiresTrendResponse?> GetNewHiresTrendAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetNewHiresTrendResponse>(
                $"api/companies/{companyId}/employees/new-hires-trend", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<GetRecentEmployeeChangesResponse?> GetRecentEmployeeChangesAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetRecentEmployeeChangesResponse>(
                $"api/companies/{companyId}/employees/recent-changes", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<GetMyTeamResponse?> GetMyTeamAsync(Guid companyId, bool includeIndirect)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetMyTeamResponse>(
                $"api/companies/{companyId}/employees/me/team?includeIndirect={includeIndirect}", HrApiJsonOptions.Default);
        }
        catch
        {
            return null;
        }
    }

    // DSH-03: non-swallowing sibling of GetMyTeamAsync.
    public Task<GetMyTeamResponse?> GetMyTeamOrThrowAsync(Guid companyId, bool includeIndirect) =>
        Http.GetFromJsonAsync<GetMyTeamResponse>(
            $"api/companies/{companyId}/employees/me/team?includeIndirect={includeIndirect}", HrApiJsonOptions.Default);

    // DSH-05: authoritative server-computed team status summary (counts + drill-down members
    // from one payload, so headline counts and lists always agree). Non-swallowing.
    public Task<TeamStatusSummaryResponse?> GetTeamStatusSummaryOrThrowAsync(Guid companyId, Guid managerId) =>
        Http.GetFromJsonAsync<TeamStatusSummaryResponse>(
            $"api/companies/{companyId}/employees/{managerId}/team-status-summary", HrApiJsonOptions.Default);

    public async Task<GetEmployeeResponse?> GetEmployeeAsync(Guid companyId, Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetEmployeeResponse>(
                $"api/companies/{companyId}/employees/{id}", HrApiJsonOptions.Default);
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool Success, string? ConflictMessage)> UpdateEmployeeProfileAsync(
        Guid companyId, Guid id, UpdateEmployeeProfileRequest request)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/employees/{id}/profile", request);

        if (response.IsSuccessStatusCode)
            return (true, null);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (false, body?.Error ?? "A conflict occurred.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (false, body?.Error ?? "Validation failed.");
        }

        return (false, "Failed to save profile.");
    }

    public async Task<(bool Success, string? Error)> CompleteInitialSetupAsync(
        Guid companyId,
        CompleteInitialEmployeeSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PutAsJsonAsync(
                $"api/companies/{companyId}/employees/me/complete-initial-setup", request, cancellationToken);

            if (response.IsSuccessStatusCode)
                return (true, null);

            // A 409 here means setup was already completed (e.g. a double-submit/race) — treat it
            // as a soft success rather than an error so the caller just closes the dialog.
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                return (true, null);

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            if (TryDeserialize<ErrorEnvelope>(raw)?.Error is { } businessMessage)
                return (false, businessMessage);

            if (TryDeserialize<ValidationErrorResponse>(raw)?.Errors is { Count: > 0 } fieldErrors)
                return (false, string.Join(" ", fieldErrors.Values.SelectMany(m => m)));

            return (false, $"Failed to complete your profile ({(int)response.StatusCode} {response.StatusCode}).");
        }
        catch { return (false, "An unexpected error occurred."); }
    }

    public async Task<GetMyPersonalDetailsResponse?> GetMyPersonalDetailsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetMyPersonalDetailsResponse>(
                $"api/companies/{companyId}/employees/me/personal-details", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<(Guid? TaskId, string? Error)> RequestPersonalDetailsChangeAsync(
        Guid companyId,
        Guid employeeId,
        string notes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/personal-details-change-requests",
                new RequestPersonalDetailsChangeRequest(notes),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, "Unable to submit your request. Please try again.");

            var result = await response.Content
                .ReadFromJsonAsync<RequestPersonalDetailsChangeResponse>(cancellationToken);
            return (result?.TaskId, null);
        }
        catch { return (null, "An unexpected error occurred."); }
    }

    public async Task<GetMyContactDetailsResponse?> GetMyContactDetailsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetMyContactDetailsResponse>(
                $"api/companies/{companyId}/employees/me/contact-details", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<(bool Success, string? Error)> UpdateMyContactDetailsAsync(
        Guid companyId,
        UpdateMyContactDetailsRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PutAsJsonAsync(
                $"api/companies/{companyId}/employees/me/contact-details", request, cancellationToken);

            if (response.IsSuccessStatusCode)
                return (true, null);

            if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                var body = await response.Content.ReadFromJsonAsync<ValidationErrorEnvelope>(cancellationToken);
                var first = body?.Errors?.Values.SelectMany(v => v).FirstOrDefault();
                return (false, first ?? "Validation failed.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(cancellationToken);
                return (false, body?.Error ?? "Validation failed.");
            }

            return (false, "Failed to save contact details.");
        }
        catch { return (false, "An unexpected error occurred."); }
    }

    public async Task<GetEmergencyContactsResponse?> GetMyEmergencyContactsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetEmergencyContactsResponse>(
                $"api/companies/{companyId}/employees/me/emergency-contacts", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<(EmergencyContactItem? Contact, string? Error)> AddMyEmergencyContactAsync(
        Guid companyId,
        AddEmergencyContactRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/me/emergency-contacts", request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var created = await response.Content.ReadFromJsonAsync<EmergencyContactItem>(cancellationToken);
                return (created, null);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                var body = await response.Content.ReadFromJsonAsync<ValidationErrorEnvelope>(cancellationToken);
                var first = body?.Errors?.Values.SelectMany(v => v).FirstOrDefault();
                return (null, first ?? "Validation failed.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(cancellationToken);
                return (null, body?.Error ?? "Validation failed.");
            }

            return (null, "Failed to add emergency contact.");
        }
        catch { return (null, "An unexpected error occurred."); }
    }

    public async Task<(bool Success, string? Error)> UpdateMyEmergencyContactAsync(
        Guid companyId,
        UpdateEmergencyContactRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PutAsJsonAsync(
                $"api/companies/{companyId}/employees/me/emergency-contacts/{request.ContactId}",
                request, cancellationToken);

            if (response.IsSuccessStatusCode)
                return (true, null);

            if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                var body = await response.Content.ReadFromJsonAsync<ValidationErrorEnvelope>(cancellationToken);
                var first = body?.Errors?.Values.SelectMany(v => v).FirstOrDefault();
                return (false, first ?? "Validation failed.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(cancellationToken);
                return (false, body?.Error ?? "Validation failed.");
            }

            return (false, "Failed to update emergency contact.");
        }
        catch { return (false, "An unexpected error occurred."); }
    }

    public async Task<(bool Success, string? Error)> RemoveMyEmergencyContactAsync(
        Guid companyId,
        Guid contactId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.DeleteAsync(
                $"api/companies/{companyId}/employees/me/emergency-contacts/{contactId}",
                cancellationToken);

            return response.IsSuccessStatusCode
                ? (true, null)
                : (false, "Failed to remove emergency contact.");
        }
        catch { return (false, "An unexpected error occurred."); }
    }

    public async Task<GetEmergencyContactsResponse?> GetEmployeeEmergencyContactsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetEmergencyContactsResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/emergency-contacts", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    private sealed record ValidationErrorEnvelope(Dictionary<string, string[]>? Errors);

    public async Task<ListNationalitiesResponse?> ListNationalitiesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<ListNationalitiesResponse>(
                "api/nationalities", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<(bool Success, string? Error)> UpdateEmploymentDetailsAsync(
        Guid companyId, Guid id, UpdateEmploymentDetailsRequest request)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/employees/{id}/employment", request);

        if (response.IsSuccessStatusCode)
            return (true, null);

        // The endpoint sends { error: "..." } for business-rule failures (not-found, conflict, a
        // plain validation rejection like "Cannot set employment status to Draft."). But FluentValidation
        // failures never reach the endpoint's own HandleAsync at all — this project sets
        // Errors.StatusCode = 422 for those (see Program.cs) and FastEndpoints short-circuits with
        // its own { statusCode, message, errors: { field: [...] } } shape instead. Falling back to a
        // single generic message on anything that isn't the { error } shape silently swallowed real
        // rejections like "Employee number is required." — check both known shapes before giving up.
        var raw = await response.Content.ReadAsStringAsync();

        if (TryDeserialize<ErrorEnvelope>(raw)?.Error is { } businessMessage)
            return (false, businessMessage);

        if (TryDeserialize<ValidationErrorResponse>(raw)?.Errors is { Count: > 0 } fieldErrors)
            return (false, string.Join(" ", fieldErrors.Values.SelectMany(m => m)));

        return (false, $"Failed to save employment details ({(int)response.StatusCode} {response.StatusCode}).");
    }

    private static T? TryDeserialize<T>(string json) where T : class
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<T>(json, HrApiJsonOptions.Default); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private sealed record ValidationErrorResponse(Dictionary<string, List<string>>? Errors);

    public async Task<(CreateEmployeeResponse? Employee, string? Error)> CreateEmployeeAsync(
        Guid companyId, CreateEmployeeRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateEmployeeResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "An employee with that email already exists.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "Validation failed.");
        }

        return (null, "Failed to create employee.");
    }

    public async Task<(StartLeavingProcessResponse? Result, string? Error)> StartLeavingProcessAsync(
        Guid companyId, Guid employeeId, StartLeavingProcessRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/leaving-process", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<StartLeavingProcessResponse>(HrApiJsonOptions.Default);
            return (created, null);
        }

        // Same reasoning as UpdateEmploymentDetailsAsync above — 404 (not found)/409 (conflict —
        // already an in-progress leaving process) send the { error } shape, but FluentValidation
        // failures short-circuit with FastEndpoints' own 422 { statusCode, message, errors } shape
        // instead. Check both known shapes before falling back to a generic message.
        var raw = await response.Content.ReadAsStringAsync();

        if (TryDeserialize<ErrorEnvelope>(raw)?.Error is { } businessMessage)
            return (null, businessMessage);

        if (TryDeserialize<ValidationErrorResponse>(raw)?.Errors is { Count: > 0 } fieldErrors)
            return (null, string.Join(" ", fieldErrors.Values.SelectMany(m => m)));

        return (null, $"Failed to start leaving process ({(int)response.StatusCode} {response.StatusCode}).");
    }

    public async Task<LeavingProcessResponse?> GetLeavingProcessAsync(Guid companyId, Guid employeeId)
    {
        try
        {
            return await Http.GetFromJsonAsync<LeavingProcessResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/leaving-process", HrApiJsonOptions.Default);
        }
        catch
        {
            return null;
        }
    }

    public async Task<(AmendLeavingProcessResponse? Result, string? Error)> AmendLeavingProcessAsync(
        Guid companyId, Guid employeeId, AmendLeavingProcessRequest request)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/leaving-process", request);

        if (response.IsSuccessStatusCode)
        {
            var amended = await response.Content.ReadFromJsonAsync<AmendLeavingProcessResponse>(HrApiJsonOptions.Default);
            return (amended, null);
        }

        // Same reasoning as StartLeavingProcessAsync above — 404 (no in-progress leaving process)/409
        // (other business conflict) send the { error } shape, but FluentValidation failures short-circuit
        // with FastEndpoints' own 422 { statusCode, message, errors } shape instead.
        var raw = await response.Content.ReadAsStringAsync();

        if (TryDeserialize<ErrorEnvelope>(raw)?.Error is { } businessMessage)
            return (null, businessMessage);

        if (TryDeserialize<ValidationErrorResponse>(raw)?.Errors is { Count: > 0 } fieldErrors)
            return (null, string.Join(" ", fieldErrors.Values.SelectMany(m => m)));

        return (null, $"Failed to amend leaving process ({(int)response.StatusCode} {response.StatusCode}).");
    }

    public async Task<(CancelLeavingProcessResponse? Result, string? Error)> CancelLeavingProcessAsync(
        Guid companyId, Guid employeeId, CancelLeavingProcessRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/leaving-process/cancel", request);

        if (response.IsSuccessStatusCode)
        {
            var cancelled = await response.Content.ReadFromJsonAsync<CancelLeavingProcessResponse>(HrApiJsonOptions.Default);
            return (cancelled, null);
        }

        var raw = await response.Content.ReadAsStringAsync();

        if (TryDeserialize<ErrorEnvelope>(raw)?.Error is { } businessMessage)
            return (null, businessMessage);

        if (TryDeserialize<ValidationErrorResponse>(raw)?.Errors is { Count: > 0 } fieldErrors)
            return (null, string.Join(" ", fieldErrors.Values.SelectMany(m => m)));

        return (null, $"Failed to cancel leaving process ({(int)response.StatusCode} {response.StatusCode}).");
    }

    private sealed record ErrorEnvelope(string? Error);
}
