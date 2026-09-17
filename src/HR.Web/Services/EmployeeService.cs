using HR.SharedKernel;
using HR.SharedKernel.Http;
using HR.Web.Models;
using System.Web;

namespace HR.Web.Services;

public class EmployeeService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    // Maps the shared ApiResult<T> failure classification onto the pre-existing ApiSaveResult
    // shape consumed by EditSectionBase/EditPageBase/SaveConflictBanner across ~25 call sites.
    // IsConcurrencyConflict is driven exclusively by ApiFailureKind.Concurrency (code == "concurrency"),
    // the same convention EditSectionBase already used.
    private static ApiSaveResult ToSaveResult<T>(ApiResult<T> result, Func<T?, int?>? versionSelector = null)
        => result.Success
            ? ApiSaveResult.Ok(versionSelector is not null ? versionSelector(result.Value) : null)
            : ApiSaveResult.Fail(result.DisplayMessage ?? "Failed to save.", result.IsConcurrencyConflict);

    /// <summary>
    /// The full employee administration list (EmployeeList grid). API-gated to "employee:manage"
    /// (HR Administrator only). Non-HR-admin pickers must call
    /// <see cref="ListSelectableEmployeesAsync"/> instead.
    /// </summary>
    public Task<ListEmployeesResponse?> ListEmployeesAsync(
        Guid companyId,
        string? search = null,
        int pageNumber = 1,
        int pageSize = 20,
        Guid? departmentId = null,
        string? status = null,
        Guid? managerId = null,
        Guid? locationId = null)
        => GetEmployeeListAsync("employees", companyId, search, pageNumber, pageSize, departmentId, status, managerId, locationId);

    /// <summary>
    /// The read-only "pick a person" projection, API-gated to "employee:read" (Manager / Recruiter /
    /// HR Administrator). Same response shape as <see cref="ListEmployeesAsync"/>; use this for
    /// dropdown/combobox pickers where the caller may be a Manager or Recruiter.
    /// </summary>
    public Task<ListEmployeesResponse?> ListSelectableEmployeesAsync(
        Guid companyId,
        string? search = null,
        int pageNumber = 1,
        int pageSize = 20,
        Guid? departmentId = null,
        string? status = null,
        Guid? managerId = null,
        Guid? locationId = null)
        => GetEmployeeListAsync("employees/selectable", companyId, search, pageNumber, pageSize, departmentId, status, managerId, locationId);

    private async Task<ListEmployeesResponse?> GetEmployeeListAsync(
        string resource,
        Guid companyId,
        string? search,
        int pageNumber,
        int pageSize,
        Guid? departmentId,
        string? status,
        Guid? managerId,
        Guid? locationId)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        search = FormText.OptionalSearch(search);
        if (search is not null) query["search"] = search;
        query["pageNumber"] = pageNumber.ToString();
        query["pageSize"] = pageSize.ToString();
        if (departmentId is not null) query["departmentId"] = departmentId.ToString();
        if (!string.IsNullOrWhiteSpace(status)) query["status"] = status;
        if (managerId is not null) query["managerId"] = managerId.ToString();
        if (locationId is not null) query["locationId"] = locationId.ToString();

        try
        {
            return await Http.GetFromJsonAsync<ListEmployeesResponse>(
                $"api/companies/{companyId}/{resource}?{query}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<EmployeeDirectorySearchResponse?> SearchEmployeeDirectoryAsync(
        Guid companyId, string? term, bool includeLeavers, int limit = 20, CancellationToken ct = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        term = FormText.OptionalSearch(term);
        if (term is not null) query["term"] = term;
        query["includeLeavers"] = includeLeavers ? "true" : "false";
        query["limit"] = limit.ToString();

        try
        {
            return await Http.GetFromJsonAsync<EmployeeDirectorySearchResponse>(
                $"api/companies/{companyId}/employees/directory-search?{query}", HrApiJsonOptions.Default, ct);
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

    public async Task<ApiSaveResult> UpdateEmployeeProfileAsync(
        Guid companyId, Guid id, UpdateEmployeeProfileRequest request)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/employees/{id}/profile", request);
        var result = await ApiResponseReader.ReadJsonAsync<UpdateEmployeeProfileResponse>(response, HrApiJsonOptions.Default);
        return ToSaveResult(result, v => v?.Version);
    }

    // Item 5: atomic combined save for the Employee Edit screen (profile + employment in one
    // transaction, one version guarding the shared Employee aggregate).
    public async Task<ApiSaveResult> UpdateEmployeeProfileAndEmploymentAsync(
        Guid companyId, Guid id, UpdateEmployeeProfileAndEmploymentRequest request)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/employees/{id}/profile-and-employment", request);
        var result = await ApiResponseReader.ReadJsonAsync<UpdateEmployeeProfileAndEmploymentResponse>(response, HrApiJsonOptions.Default);
        return ToSaveResult(result, v => v?.Version);
    }

    public async Task<(bool Success, string? Error)> CompleteInitialSetupAsync(
        Guid companyId,
        CompleteInitialEmployeeSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/employees/me/complete-initial-setup", request, cancellationToken);

        // A 409 here means setup was already completed (e.g. a double-submit/race) — treat it
        // as a soft success rather than an error so the caller just closes the dialog.
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return (true, null);

        var result = await ApiResponseReader.ReadNoContentAsync(response, cancellationToken);
        return result.Success
            ? (true, null)
            : (false, result.DisplayMessage ?? "Failed to complete your profile.");
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

    public async Task<ApiSaveResult> UpdateMyContactDetailsAsync(
        Guid companyId,
        UpdateMyContactDetailsRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/employees/me/contact-details", request, cancellationToken);
        var result = await ApiResponseReader.ReadJsonAsync<GetMyContactDetailsResponse>(
            response, HrApiJsonOptions.Default, cancellationToken);
        return ToSaveResult(result, v => v?.Version);
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
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees/me/emergency-contacts", request, cancellationToken);
        var result = await ApiResponseReader.ReadJsonAsync<EmergencyContactItem>(response, cancellationToken: cancellationToken);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to add emergency contact."));
    }

    public async Task<(bool Success, string? Error)> UpdateMyEmergencyContactAsync(
        Guid companyId,
        UpdateEmergencyContactRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/employees/me/emergency-contacts/{request.ContactId}",
            request, cancellationToken);
        var result = await ApiResponseReader.ReadNoContentAsync(response, cancellationToken);
        return (result.Success, result.Success ? null : (result.DisplayMessage ?? "Failed to update emergency contact."));
    }

    public async Task<(bool Success, string? Error)> RemoveMyEmergencyContactAsync(
        Guid companyId,
        Guid contactId,
        CancellationToken cancellationToken = default)
    {
        var response = await Http.DeleteAsync(
            $"api/companies/{companyId}/employees/me/emergency-contacts/{contactId}",
            cancellationToken);
        var result = await ApiResponseReader.ReadNoContentAsync(response, cancellationToken);
        return (result.Success, result.Success ? null : (result.DisplayMessage ?? "Failed to remove emergency contact."));
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

    public async Task<ApiSaveResult> UpdateEmploymentDetailsAsync(
        Guid companyId, Guid id, UpdateEmploymentDetailsRequest request)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/employees/{id}/employment", request);
        var result = await ApiResponseReader.ReadJsonAsync<EmploymentDetailsSaveBody>(response, HrApiJsonOptions.Default);
        return ToSaveResult(result, v => v?.Version);
    }

    private sealed record EmploymentDetailsSaveBody(int Version);

    public async Task<(CreateEmployeeResponse? Employee, string? Error)> CreateEmployeeAsync(
        Guid companyId, CreateEmployeeRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees", request);
        var result = await ApiResponseReader.ReadJsonAsync<CreateEmployeeResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to create employee."));
    }

    public async Task<(StartLeavingProcessResponse? Result, string? Error)> StartLeavingProcessAsync(
        Guid companyId, Guid employeeId, StartLeavingProcessRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/leaving-process", request);
        var result = await ApiResponseReader.ReadJsonAsync<StartLeavingProcessResponse>(response, HrApiJsonOptions.Default);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to start leaving process."));
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

    public async Task<AmendLeavingProcessResult> AmendLeavingProcessAsync(
        Guid companyId, Guid employeeId, AmendLeavingProcessRequest request)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/leaving-process", request);
        var result = await ApiResponseReader.ReadJsonAsync<AmendLeavingProcessResponse>(response, HrApiJsonOptions.Default);

        if (result.Success)
            return new(result.Value, null, false, result.Value?.Version);

        // Ticket 2: distinguish an optimistic-concurrency 409 (code == "concurrency") so the dialog
        // can raise the shared <SaveConflictBanner> rather than a generic error.
        if (result.IsConcurrencyConflict)
            return new(null, result.Error ?? "Someone else changed this leaving process while you were editing.", true);

        return new(null, result.DisplayMessage ?? "Failed to amend leaving process.");
    }

    public async Task<(CancelLeavingProcessResponse? Result, string? Error)> CancelLeavingProcessAsync(
        Guid companyId, Guid employeeId, CancelLeavingProcessRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/leaving-process/cancel", request);
        var result = await ApiResponseReader.ReadJsonAsync<CancelLeavingProcessResponse>(response, HrApiJsonOptions.Default);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to cancel leaving process."));
    }

    // ── EQUALITY & DIVERSITY (self-service) ───────────────────────────────────

    public async Task<GetMyEqualityDataResponse?> GetMyEqualityRecordAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetMyEqualityDataResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/equality-record",
                HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<(bool Success, string? Error)> SaveMyEqualityRecordAsync(
        Guid companyId,
        Guid employeeId,
        SaveMyEqualityDataRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/equality-record",
            request, HrApiJsonOptions.Default, cancellationToken);
        var result = await ApiResponseReader.ReadNoContentAsync(response, cancellationToken);
        return (result.Success, result.Success ? null : (result.DisplayMessage ?? "Failed to save your equality and diversity information."));
    }

    public async Task<(bool Success, string? Error)> ClearMyEqualityRecordAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var response = await Http.DeleteAsync(
            $"api/companies/{companyId}/employees/{employeeId}/equality-record",
            cancellationToken);
        var result = await ApiResponseReader.ReadNoContentAsync(response, cancellationToken);
        return (result.Success, result.Success ? null : (result.DisplayMessage ?? "Failed to clear your equality and diversity information."));
    }
}

// Ticket 2: unified save outcome for concurrency-aware API calls. IsConcurrencyConflict is true
// only when the API rejected the save with a 409 whose code is "concurrency" (the record changed
// since it was loaded) — as opposed to a plain business conflict such as a duplicate email.
// NewVersion carries the post-save optimistic-concurrency token so a caller performing a second
// sequential save (EmployeeEdit.razor saves the profile then the employment tab) can send the
// fresh token. Consumed by EditSectionBase/EditPageBase via ApplySaveResult.
public sealed record ApiSaveResult(bool Success, string? ErrorMessage, bool IsConcurrencyConflict, int? NewVersion)
{
    public static ApiSaveResult Ok(int? newVersion) => new(true, null, false, newVersion);
    public static ApiSaveResult Fail(string message, bool isConcurrencyConflict = false)
        => new(false, message, isConcurrencyConflict, null);
}

// Ticket 2: result of an Amend Leaving Process call. Deconstructs to (Result, Error) so the
// existing call site keeps working, while IsConcurrencyConflict lets the dialog raise the shared
// SaveConflictBanner on a stale-version 409. Mirrors CompensationService.UpdateFutureCompensationResult.
public sealed record AmendLeavingProcessResult(
    AmendLeavingProcessResponse? Result,
    string? Error,
    bool IsConcurrencyConflict = false,
    int? NewVersion = null)
{
    public void Deconstruct(out AmendLeavingProcessResponse? result, out string? error)
    {
        result = Result;
        error = Error;
    }
}
