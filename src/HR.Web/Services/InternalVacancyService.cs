using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HR.SharedKernel.Http;
using HR.Web.Models;
using Microsoft.AspNetCore.Components.Forms;
using System.Web;

namespace HR.Web.Services;

public class InternalVacancyService(HrApiHttpClientFactory httpClientFactory)
{
    private const string AlreadyAppliedCode = "already_applied";
    private const string ApplicantEmailInUseCode = "applicant_email_in_use";
    private const string NotEligibleToApplyCode = "not_eligible_to_apply";

    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<InternalVacancyListResponse?> ListAsync(Guid companyId, string? search)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;
        var qs = query.Count > 0 ? $"?{query}" : string.Empty;

        try
        {
            return await Http.GetFromJsonAsync<InternalVacancyListResponse>(
                $"api/companies/{companyId}/internal-vacancies{qs}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<InternalVacancyDetail?> GetAsync(Guid companyId, Guid vacancyId)
    {
        try
        {
            return await Http.GetFromJsonAsync<InternalVacancyDetail>(
                $"api/companies/{companyId}/internal-vacancies/{vacancyId}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<InternalVacancyApplyResult> ApplyAsync(
        Guid companyId, Guid vacancyId, IBrowserFile cvFile, CancellationToken cancellationToken = default)
    {
        if (cvFile.Size > CandidateService.MaxCandidateDocumentBytes)
            return Failed("The CV file is larger than the 20 MB limit.");
        if (!string.Equals(Path.GetExtension(cvFile.Name), ".pdf", StringComparison.OrdinalIgnoreCase) || !CandidateService.IsPdfContentType(cvFile.ContentType))
            return Failed("The CV must be a PDF file.");

        using var content = new MultipartFormDataContent();
        Stream? cvStream = null;
        try
        {
            HttpResponseMessage response;
            try
            {
                cvStream = cvFile.OpenReadStream(maxAllowedSize: CandidateService.MaxCandidateDocumentBytes, cancellationToken);
                var fileContent = new StreamContent(cvStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                    string.IsNullOrWhiteSpace(cvFile.ContentType) ? "application/pdf" : cvFile.ContentType);
                content.Add(fileContent, "CvFile", cvFile.Name);

                response = await Http.PostAsync(
                    $"api/companies/{companyId}/internal-vacancies/{vacancyId}/applications", content, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return Failed("Unable to reach the server. Please check your connection and try again.");
            }
            catch (OperationCanceledException)
            {
                return Failed("The request timed out. Please try again.");
            }
            catch (IOException)
            {
                return Failed("The selected CV file could not be read. Please choose it again.");
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    var forbiddenCode = await ReadErrorCodeAsync(response, cancellationToken);
                    return new InternalVacancyApplyResult(InternalVacancyApplyOutcome.NotEligible,
                        forbiddenCode == NotEligibleToApplyCode
                            ? "Only current employees can apply for internal vacancies. Please contact HR if you think this is wrong."
                            : "You can't apply for this vacancy from this account.");
                }

                var result = await ApiResponseReader.ReadNoContentAsync(response, cancellationToken);
                if (result.Success)
                    return new InternalVacancyApplyResult(InternalVacancyApplyOutcome.Submitted, null);

                switch (result.FailureKind)
                {
                    case ApiFailureKind.Conflict when result.Code == AlreadyAppliedCode:
                        return new InternalVacancyApplyResult(InternalVacancyApplyOutcome.AlreadyApplied,
                            "You have already applied for this vacancy.");

                    case ApiFailureKind.Conflict when result.Code == ApplicantEmailInUseCode:
                        return new InternalVacancyApplyResult(InternalVacancyApplyOutcome.EmailInUse,
                            "We couldn't submit your application because your work email is already linked to another candidate record. Please contact HR to resolve this.");

                    case ApiFailureKind.NotFound:
                        return new InternalVacancyApplyResult(InternalVacancyApplyOutcome.VacancyUnavailable,
                            "This vacancy is no longer open for applications.");

                    case ApiFailureKind.Validation:
                        return Failed(string.IsNullOrWhiteSpace(result.DisplayMessage)
                            ? "Your CV could not be accepted. Please check the file and try again."
                            : result.DisplayMessage);

                    case ApiFailureKind.Unauthenticated:
                        return Failed(result.DisplayMessage ?? "Your session has expired. Please sign in again.");

                    default:
                        return Failed("Your application could not be submitted. Please try again.");
                }
            }
        }
        finally
        {
            if (cvStream is not null)
                await cvStream.DisposeAsync();
        }
    }

    private static InternalVacancyApplyResult Failed(string message) =>
        new(InternalVacancyApplyOutcome.Failed, message);

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ApiErrorEnvelope>(raw, HrApiJsonOptions.Default)?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
