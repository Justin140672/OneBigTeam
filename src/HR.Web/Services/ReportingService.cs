using System.Net;
using HR.Infrastructure.Abstractions;
using HR.Web.Models;
using System.Web;

namespace HR.Web.Services;

public class ReportingService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<GetReportCatalogResponse?> GetReportCatalogAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetReportCatalogResponse>(
                $"api/companies/{companyId}/reporting/catalog", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetEmployeeDirectoryReportResponse?> GetEmployeeDirectoryReportAsync(
        Guid companyId, EmployeeDirectoryReportFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            return await Http.GetFromJsonAsync<GetEmployeeDirectoryReportResponse>(
                $"api/companies/{companyId}/reporting/employee-directory?{query}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportEmployeeDirectoryReportAsync(
        Guid companyId, EmployeeDirectoryReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            query += $"&format={format}";

            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/employee-directory/export?{query}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the employee directory report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the employee directory report.");
        }
    }

    private static string BuildQuery(EmployeeDirectoryReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        if (filter.DepartmentId is not null) query["departmentId"] = filter.DepartmentId.ToString();
        if (filter.LocationId is not null) query["locationId"] = filter.LocationId.ToString();
        if (filter.PositionProfileId is not null) query["positionProfileId"] = filter.PositionProfileId.ToString();
        if (filter.ManagerId is not null) query["managerId"] = filter.ManagerId.ToString();
        if (filter.EmploymentTypeId is not null) query["employmentTypeId"] = filter.EmploymentTypeId.ToString();
        if (filter.DateRangeStart is not null) query["dateRangeStart"] = filter.DateRangeStart.Value.ToString("yyyy-MM-dd");
        if (filter.DateRangeEnd is not null) query["dateRangeEnd"] = filter.DateRangeEnd.Value.ToString("yyyy-MM-dd");
        if (!string.IsNullOrWhiteSpace(filter.EmployeeStatus)) query["employeeStatus"] = filter.EmployeeStatus;
        query["page"] = filter.Page.ToString();
        query["pageSize"] = filter.PageSize.ToString();
        if (!string.IsNullOrWhiteSpace(filter.SortBy)) query["sortBy"] = filter.SortBy;
        query["sortDescending"] = filter.SortDescending.ToString();

        return query.ToString() ?? string.Empty;
    }

    // ── Employee Starter report ──────────────────────────────────────────────

    public async Task<GetEmployeeStarterReportResponse?> GetEmployeeStarterReportAsync(
        Guid companyId, EmployeeStarterReportFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            return await Http.GetFromJsonAsync<GetEmployeeStarterReportResponse>(
                $"api/companies/{companyId}/reporting/employee-starters?{query}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportEmployeeStarterReportAsync(
        Guid companyId, EmployeeStarterReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            query += $"&format={format}";

            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/employee-starters/export?{query}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the employee starter report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the employee starter report.");
        }
    }

    private static string BuildQuery(EmployeeStarterReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        if (filter.DepartmentId is not null) query["departmentId"] = filter.DepartmentId.ToString();
        if (filter.LocationId is not null) query["locationId"] = filter.LocationId.ToString();
        if (filter.PositionProfileId is not null) query["positionProfileId"] = filter.PositionProfileId.ToString();
        if (filter.EmploymentTypeId is not null) query["employmentTypeId"] = filter.EmploymentTypeId.ToString();
        if (filter.DateRangeStart is not null) query["dateRangeStart"] = filter.DateRangeStart.Value.ToString("yyyy-MM-dd");
        if (filter.DateRangeEnd is not null) query["dateRangeEnd"] = filter.DateRangeEnd.Value.ToString("yyyy-MM-dd");
        query["page"] = filter.Page.ToString();
        query["pageSize"] = filter.PageSize.ToString();
        if (!string.IsNullOrWhiteSpace(filter.SortBy)) query["sortBy"] = filter.SortBy;
        query["sortDescending"] = filter.SortDescending.ToString();

        return query.ToString() ?? string.Empty;
    }

    // ── Employee Leaver report ───────────────────────────────────────────────

    public async Task<GetEmployeeLeaverReportResponse?> GetEmployeeLeaverReportAsync(
        Guid companyId, EmployeeLeaverReportFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            return await Http.GetFromJsonAsync<GetEmployeeLeaverReportResponse>(
                $"api/companies/{companyId}/reporting/employee-leavers?{query}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportEmployeeLeaverReportAsync(
        Guid companyId, EmployeeLeaverReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            query += $"&format={format}";

            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/employee-leavers/export?{query}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the employee leaver report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the employee leaver report.");
        }
    }

    private static string BuildQuery(EmployeeLeaverReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        if (filter.DepartmentId is not null) query["departmentId"] = filter.DepartmentId.ToString();
        if (filter.PositionProfileId is not null) query["positionProfileId"] = filter.PositionProfileId.ToString();
        if (filter.DateRangeStart is not null) query["dateRangeStart"] = filter.DateRangeStart.Value.ToString("yyyy-MM-dd");
        if (filter.DateRangeEnd is not null) query["dateRangeEnd"] = filter.DateRangeEnd.Value.ToString("yyyy-MM-dd");
        query["page"] = filter.Page.ToString();
        query["pageSize"] = filter.PageSize.ToString();
        if (!string.IsNullOrWhiteSpace(filter.SortBy)) query["sortBy"] = filter.SortBy;
        query["sortDescending"] = filter.SortDescending.ToString();

        return query.ToString() ?? string.Empty;
    }

    // ── Leave Summary report ─────────────────────────────────────────────────

    public async Task<GetLeaveSummaryReportResponse?> GetLeaveSummaryReportAsync(
        Guid companyId, LeaveSummaryReportFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            return await Http.GetFromJsonAsync<GetLeaveSummaryReportResponse>(
                $"api/companies/{companyId}/reporting/leave-summary?{query}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportLeaveSummaryReportAsync(
        Guid companyId, LeaveSummaryReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            query += $"&format={format}";

            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/leave-summary/export?{query}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the leave summary report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the leave summary report.");
        }
    }

    private static string BuildQuery(LeaveSummaryReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        query["policyYear"] = filter.PolicyYear.ToString();
        if (filter.DepartmentId is not null) query["departmentId"] = filter.DepartmentId.ToString();
        if (filter.LeaveTypeId is not null) query["leaveTypeId"] = filter.LeaveTypeId.ToString();
        query["groupBy"] = filter.GroupBy.ToString();

        return query.ToString() ?? string.Empty;
    }

    // ── Leave Calendar report ────────────────────────────────────────────────

    public async Task<GetLeaveCalendarReportResponse?> GetLeaveCalendarReportAsync(
        Guid companyId, LeaveCalendarReportFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            return await Http.GetFromJsonAsync<GetLeaveCalendarReportResponse>(
                $"api/companies/{companyId}/reporting/leave-calendar?{query}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportLeaveCalendarReportAsync(
        Guid companyId, LeaveCalendarReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            query += $"&format={format}";

            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/leave-calendar/export?{query}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the leave calendar report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the leave calendar report.");
        }
    }

    private static string BuildQuery(LeaveCalendarReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        query["year"] = filter.Year.ToString();
        query["month"] = filter.Month.ToString();
        if (filter.DepartmentId is not null) query["departmentId"] = filter.DepartmentId.ToString();

        return query.ToString() ?? string.Empty;
    }

    // ── Sickness report ───────────────────────────────────────────────────────

    public async Task<GetSicknessReportResponse?> GetSicknessReportAsync(
        Guid companyId, SicknessReportFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            return await Http.GetFromJsonAsync<GetSicknessReportResponse>(
                $"api/companies/{companyId}/reporting/sickness?{query}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportSicknessReportAsync(
        Guid companyId, SicknessReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            query += $"&format={format}";

            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/sickness/export?{query}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the sickness report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the sickness report.");
        }
    }

    private static string BuildQuery(SicknessReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        if (filter.StartDate is not null) query["startDate"] = filter.StartDate.Value.ToString("yyyy-MM-dd");
        if (filter.EndDate is not null) query["endDate"] = filter.EndDate.Value.ToString("yyyy-MM-dd");
        query["groupBy"] = filter.GroupBy.ToString();

        return query.ToString() ?? string.Empty;
    }

    // ── Recruitment Pipeline report ──────────────────────────────────────────

    public async Task<(GetRecruitmentPipelineReportResponse? Response, string? Error)> GetRecruitmentPipelineReportAsync(
        Guid companyId, RecruitmentPipelineReportFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            var response = await Http.GetFromJsonAsync<GetRecruitmentPipelineReportResponse>(
                $"api/companies/{companyId}/reporting/recruitment-pipeline?{query}", HrApiJsonOptions.Default, cancellationToken);
            return (response, null);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return (null, "You do not have permission to view this report.");
        }
        catch (HttpRequestException)
        {
            return (null, "Failed to load the recruitment pipeline report.");
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportRecruitmentPipelineReportAsync(
        Guid companyId, RecruitmentPipelineReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            query += $"&format={format}";

            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/recruitment-pipeline/export?{query}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the recruitment pipeline report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the recruitment pipeline report.");
        }
    }

    private static string BuildQuery(RecruitmentPipelineReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        if (filter.StartDate is not null) query["startDate"] = filter.StartDate.Value.ToString("yyyy-MM-dd");
        if (filter.EndDate is not null) query["endDate"] = filter.EndDate.Value.ToString("yyyy-MM-dd");
        query["groupBy"] = filter.GroupBy.ToString();

        return query.ToString() ?? string.Empty;
    }

    // ── Vacancy Performance report ───────────────────────────────────────────

    public async Task<GetVacancyPerformanceReportResponse?> GetVacancyPerformanceReportAsync(
        Guid companyId, VacancyPerformanceReportFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            return await Http.GetFromJsonAsync<GetVacancyPerformanceReportResponse>(
                $"api/companies/{companyId}/reporting/vacancy-performance?{query}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportVacancyPerformanceReportAsync(
        Guid companyId, VacancyPerformanceReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            query += $"&format={format}";

            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/vacancy-performance/export?{query}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the vacancy performance report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the vacancy performance report.");
        }
    }

    private static string BuildQuery(VacancyPerformanceReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        if (filter.StartDate is not null) query["startDate"] = filter.StartDate.Value.ToString("yyyy-MM-dd");
        if (filter.EndDate is not null) query["endDate"] = filter.EndDate.Value.ToString("yyyy-MM-dd");

        return query.ToString() ?? string.Empty;
    }

    // ── Probation report ──────────────────────────────────────────────────────

    public async Task<GetProbationReportResponse?> GetProbationReportAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetProbationReportResponse>(
                $"api/companies/{companyId}/reporting/probation", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportProbationReportAsync(
        Guid companyId, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/probation/export?format={format}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the probation report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the probation report.");
        }
    }

    // ── Onboarding Progress report ───────────────────────────────────────────

    public async Task<GetOnboardingProgressReportResponse?> GetOnboardingProgressReportAsync(
        Guid companyId, OnboardingProgressReportFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            return await Http.GetFromJsonAsync<GetOnboardingProgressReportResponse>(
                $"api/companies/{companyId}/reporting/onboarding-progress?{query}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportOnboardingProgressReportAsync(
        Guid companyId, OnboardingProgressReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            query += $"&format={format}";

            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/onboarding-progress/export?{query}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the onboarding progress report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the onboarding progress report.");
        }
    }

    private static string BuildQuery(OnboardingProgressReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["overdueOnly"] = filter.OverdueOnly.ToString();
        return query.ToString() ?? string.Empty;
    }

    // ── Offboarding Progress report ──────────────────────────────────────────

    public async Task<GetOffboardingProgressReportResponse?> GetOffboardingProgressReportAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetOffboardingProgressReportResponse>(
                $"api/companies/{companyId}/reporting/offboarding-progress", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportOffboardingProgressReportAsync(
        Guid companyId, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/offboarding-progress/export?format={format}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the offboarding progress report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the offboarding progress report.");
        }
    }

    // ── Document Compliance report ───────────────────────────────────────────

    public async Task<GetDocumentComplianceReportResponse?> GetDocumentComplianceReportAsync(
        Guid companyId, DocumentComplianceReportFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            return await Http.GetFromJsonAsync<GetDocumentComplianceReportResponse>(
                $"api/companies/{companyId}/reporting/document-compliance?{query}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportDocumentComplianceReportAsync(
        Guid companyId, DocumentComplianceReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            query += $"&format={format}";

            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/document-compliance/export?{query}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the document compliance report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the document compliance report.");
        }
    }

    private static string BuildQuery(DocumentComplianceReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        if (filter.PositionProfileId is not null) query["positionProfileId"] = filter.PositionProfileId.ToString();
        return query.ToString() ?? string.Empty;
    }

    // ── Company Document Acknowledgement report ──────────────────────────────

    public async Task<GetCompanyDocumentAcknowledgementReportResponse?> GetCompanyDocumentAcknowledgementReportAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetCompanyDocumentAcknowledgementReportResponse>(
                $"api/companies/{companyId}/reporting/document-acknowledgement", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportCompanyDocumentAcknowledgementReportAsync(
        Guid companyId, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/document-acknowledgement/export?format={format}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the document acknowledgement report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the document acknowledgement report.");
        }
    }

    // ── Asset Assignment report ──────────────────────────────────────────────

    public async Task<GetAssetAssignmentReportResponse?> GetAssetAssignmentReportAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetAssetAssignmentReportResponse>(
                $"api/companies/{companyId}/reporting/asset-assignment", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportAssetAssignmentReportAsync(
        Guid companyId, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/asset-assignment/export?format={format}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the asset assignment report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the asset assignment report.");
        }
    }

    // ── Workload & HR Actions report ─────────────────────────────────────────

    public async Task<GetWorkloadActionsResponse?> GetWorkloadActionsReportAsync(
        Guid companyId, WorkloadActionsReportFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            return await Http.GetFromJsonAsync<GetWorkloadActionsResponse>(
                $"api/companies/{companyId}/reporting/workload-actions?{query}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static string BuildQuery(WorkloadActionsReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        if (!string.IsNullOrWhiteSpace(filter.ActionType)) query["actionType"] = filter.ActionType;
        if (!string.IsNullOrWhiteSpace(filter.Department)) query["department"] = filter.Department;
        if (!string.IsNullOrWhiteSpace(filter.Urgency)) query["urgency"] = filter.Urgency;
        if (!string.IsNullOrWhiteSpace(filter.Status)) query["status"] = filter.Status;
        if (filter.EmployeeId is not null) query["employeeId"] = filter.EmployeeId.ToString();
        if (filter.DueDateStart is not null) query["dueDateStart"] = filter.DueDateStart.Value.ToString("yyyy-MM-dd");
        if (filter.DueDateEnd is not null) query["dueDateEnd"] = filter.DueDateEnd.Value.ToString("yyyy-MM-dd");
        if (filter.GroupBy is not null) query["groupBy"] = filter.GroupBy.ToString();
        if (filter.ManagerId is not null) query["managerId"] = filter.ManagerId.ToString();
        if (filter.LocationId is not null) query["locationId"] = filter.LocationId.ToString();
        if (!string.IsNullOrWhiteSpace(filter.RecruitmentUser)) query["recruitmentUser"] = filter.RecruitmentUser;

        return query.ToString() ?? string.Empty;
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportWorkloadActionsReportAsync(
        Guid companyId, WorkloadActionsReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/workload-actions/export?{query}&format={format}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the workload & HR actions report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the workload & HR actions report.");
        }
    }

    // ── Recruitment Pipeline Summary report ──────────────────────────────────

    public async Task<(GetRecruitmentPipelineSummaryReportResponse? Response, string? Error)> GetRecruitmentPipelineSummaryReportAsync(
        Guid companyId, RecruitmentPipelineSummaryReportFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            var response = await Http.GetFromJsonAsync<GetRecruitmentPipelineSummaryReportResponse>(
                $"api/companies/{companyId}/reporting/recruitment-pipeline-summary?{query}", HrApiJsonOptions.Default, cancellationToken);
            return (response, null);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return (null, "You do not have permission to view this report.");
        }
        catch (HttpRequestException)
        {
            return (null, "Failed to load the recruitment pipeline summary report.");
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportRecruitmentPipelineSummaryReportAsync(
        Guid companyId, RecruitmentPipelineSummaryReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            query += $"&format={format}";

            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/recruitment-pipeline-summary/export?{query}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the recruitment pipeline summary report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the recruitment pipeline summary report.");
        }
    }

    private static string BuildQuery(RecruitmentPipelineSummaryReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["includeClosed"] = filter.IncludeClosed.ToString();
        return query.ToString() ?? string.Empty;
    }

    // ── HR Headcount Summary report ──────────────────────────────────────────

    public async Task<(GetHrHeadcountSummaryReportResponse? Response, string? Error)> GetHrHeadcountSummaryReportAsync(
        Guid companyId, HrHeadcountSummaryReportFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            var response = await Http.GetFromJsonAsync<GetHrHeadcountSummaryReportResponse>(
                $"api/companies/{companyId}/reporting/hr-headcount-summary?{query}", HrApiJsonOptions.Default, cancellationToken);
            return (response, null);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return (null, "You do not have permission to view this report.");
        }
        catch (HttpRequestException)
        {
            return (null, "Failed to load the HR headcount summary report.");
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportHrHeadcountSummaryReportAsync(
        Guid companyId, HrHeadcountSummaryReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            query += $"&format={format}";

            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/hr-headcount-summary/export?{query}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the HR headcount summary report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the HR headcount summary report.");
        }
    }

    private static string BuildQuery(HrHeadcountSummaryReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        if (filter.DepartmentId is not null) query["departmentId"] = filter.DepartmentId.ToString();
        if (filter.LocationId is not null) query["locationId"] = filter.LocationId.ToString();
        if (filter.EmploymentTypeId is not null) query["employmentTypeId"] = filter.EmploymentTypeId.ToString();
        if (!string.IsNullOrWhiteSpace(filter.EmployeeStatus)) query["employeeStatus"] = filter.EmployeeStatus;
        return query.ToString() ?? string.Empty;
    }

    // ── ADM-08 Governance reporting hub ──────────────────────────────────────

    public Task<(GetGovernanceAuditReportResponse? Response, string? Error)> GetGovernanceUserActivityReportAsync(
        Guid companyId, GovernanceAuditReportFilter filter, CancellationToken cancellationToken = default)
        => GetGovernanceAuditReportAsync(companyId, "governance/user-activity", filter, cancellationToken);

    public Task<(GetGovernanceAuditReportResponse? Response, string? Error)> GetGovernanceAdministrativeChangesReportAsync(
        Guid companyId, GovernanceAuditReportFilter filter, CancellationToken cancellationToken = default)
        => GetGovernanceAuditReportAsync(companyId, "governance/administrative-changes", filter, cancellationToken);

    public Task<(GetGovernanceAuditReportResponse? Response, string? Error)> GetGovernanceSecurityEventsReportAsync(
        Guid companyId, GovernanceAuditReportFilter filter, CancellationToken cancellationToken = default)
        => GetGovernanceAuditReportAsync(companyId, "governance/security-events", filter, cancellationToken);

    public Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportGovernanceUserActivityReportAsync(
        Guid companyId, GovernanceAuditReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
        => ExportGovernanceAuditReportAsync(companyId, "governance/user-activity", filter, format, cancellationToken);

    public Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportGovernanceAdministrativeChangesReportAsync(
        Guid companyId, GovernanceAuditReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
        => ExportGovernanceAuditReportAsync(companyId, "governance/administrative-changes", filter, format, cancellationToken);

    public Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportGovernanceSecurityEventsReportAsync(
        Guid companyId, GovernanceAuditReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
        => ExportGovernanceAuditReportAsync(companyId, "governance/security-events", filter, format, cancellationToken);

    private async Task<(GetGovernanceAuditReportResponse? Response, string? Error)> GetGovernanceAuditReportAsync(
        Guid companyId, string resource, GovernanceAuditReportFilter filter, CancellationToken cancellationToken)
    {
        try
        {
            var query = BuildQuery(filter);
            var response = await Http.GetFromJsonAsync<GetGovernanceAuditReportResponse>(
                $"api/companies/{companyId}/reporting/{resource}?{query}", HrApiJsonOptions.Default, cancellationToken);
            return (response, null);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return (null, "You do not have permission to view this report.");
        }
        catch (HttpRequestException)
        {
            return (null, "Failed to load the governance report.");
        }
    }

    private async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportGovernanceAuditReportAsync(
        Guid companyId, string resource, GovernanceAuditReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken)
    {
        try
        {
            var query = BuildQuery(filter);
            query += $"&format={format}";

            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/{resource}/export?{query}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the governance report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the governance report.");
        }
    }

    private static string BuildQuery(GovernanceAuditReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        if (filter.ActorUserId is not null) query["actorUserId"] = filter.ActorUserId.ToString();
        if (!string.IsNullOrWhiteSpace(filter.EventType)) query["eventType"] = filter.EventType;
        if (filter.EmployeeId is not null) query["employeeId"] = filter.EmployeeId.ToString();
        if (filter.FromDate is not null) query["fromDate"] = filter.FromDate.Value.ToString("yyyy-MM-dd");
        if (filter.ToDate is not null) query["toDate"] = filter.ToDate.Value.ToString("yyyy-MM-dd");
        if (!string.IsNullOrWhiteSpace(filter.Status)) query["status"] = filter.Status;
        query["page"] = filter.Page.ToString();
        query["pageSize"] = filter.PageSize.ToString();

        return query.ToString() ?? string.Empty;
    }

    public async Task<(GetGovernanceComplianceStatusReportResponse? Response, string? Error)> GetGovernanceComplianceStatusReportAsync(
        Guid companyId, GovernanceComplianceStatusReportFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            var response = await Http.GetFromJsonAsync<GetGovernanceComplianceStatusReportResponse>(
                $"api/companies/{companyId}/reporting/governance/compliance-status?{query}", HrApiJsonOptions.Default, cancellationToken);
            return (response, null);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return (null, "You do not have permission to view this report.");
        }
        catch (HttpRequestException)
        {
            return (null, "Failed to load the governance compliance status report.");
        }
    }

    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> ExportGovernanceComplianceStatusReportAsync(
        Guid companyId, GovernanceComplianceStatusReportFilter filter, ReportExportFormat format, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = BuildQuery(filter);
            query += $"&format={format}";

            var response = await Http.GetAsync(
                $"api/companies/{companyId}/reporting/governance/compliance-status/export?{query}", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "Failed to export the governance compliance status report.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

            return (bytes, contentType, fileName, null);
        }
        catch
        {
            return (null, null, null, "Failed to export the governance compliance status report.");
        }
    }

    private static string BuildQuery(GovernanceComplianceStatusReportFilter filter)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        if (!string.IsNullOrWhiteSpace(filter.Category)) query["category"] = filter.Category;
        if (!string.IsNullOrWhiteSpace(filter.Severity)) query["severity"] = filter.Severity;
        if (!string.IsNullOrWhiteSpace(filter.Department)) query["department"] = filter.Department;
        if (filter.ManagerId is not null) query["managerId"] = filter.ManagerId.ToString();
        if (filter.DueDateStart is not null) query["dueDateStart"] = filter.DueDateStart.Value.ToString("yyyy-MM-dd");
        if (filter.DueDateEnd is not null) query["dueDateEnd"] = filter.DueDateEnd.Value.ToString("yyyy-MM-dd");
        query["page"] = filter.Page.ToString();
        query["pageSize"] = filter.PageSize.ToString();

        return query.ToString() ?? string.Empty;
    }

    // ── Favourites ────────────────────────────────────────────────────────────

    public async Task<GetReportFavouritesResponse?> GetReportFavouritesAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetReportFavouritesResponse>(
                $"api/companies/{companyId}/reporting/favourites", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<bool> AddReportFavouriteAsync(
        Guid companyId, string reportId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            var response = await Http.PutAsync(
                $"api/companies/{companyId}/reporting/favourites/{reportId}", content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<bool> RemoveReportFavouriteAsync(
        Guid companyId, string reportId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.DeleteAsync(
                $"api/companies/{companyId}/reporting/favourites/{reportId}", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    // ── Saved Report Views ───────────────────────────────────────────────────

    public async Task<GetReportViewsResponse?> GetReportViewsAsync(
        Guid companyId, string reportId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetReportViewsResponse>(
                $"api/companies/{companyId}/reporting/saved-views/{reportId}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(SaveReportViewResponse? Result, string? Error)> SaveReportViewAsync(
        Guid companyId, SaveReportViewRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/reporting/saved-views", request, HrApiJsonOptions.Default, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, await ReadErrorAsync(response, "Failed to save the current filters as a view."));

            return (await response.Content.ReadFromJsonAsync<SaveReportViewResponse>(HrApiJsonOptions.Default, cancellationToken), null);
        }
        catch (HttpRequestException)
        {
            return (null, "Failed to save the current filters as a view.");
        }
    }

    public async Task<(RenameReportViewResponse? Result, string? Error)> RenameReportViewAsync(
        Guid companyId, Guid viewId, string name, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PatchAsJsonAsync(
                $"api/companies/{companyId}/reporting/saved-views/{viewId}", new RenameReportViewRequest(name), HrApiJsonOptions.Default, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, await ReadErrorAsync(response, "Failed to rename the saved view."));

            return (await response.Content.ReadFromJsonAsync<RenameReportViewResponse>(HrApiJsonOptions.Default, cancellationToken), null);
        }
        catch (HttpRequestException)
        {
            return (null, "Failed to rename the saved view.");
        }
    }

    public async Task<bool> DeleteReportViewAsync(
        Guid companyId, Guid viewId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.DeleteAsync(
                $"api/companies/{companyId}/reporting/saved-views/{viewId}", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<bool> SetDefaultReportViewAsync(
        Guid companyId, Guid viewId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            var response = await Http.PatchAsync(
                $"api/companies/{companyId}/reporting/saved-views/{viewId}/default", content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return body?.Error ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private sealed record ErrorEnvelope(string? Error);
}
