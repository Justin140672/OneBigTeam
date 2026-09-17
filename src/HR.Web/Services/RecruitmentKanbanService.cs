using HR.SharedKernel.Http;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class RecruitmentKanbanService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<GetRecruitmentKanbanResponse?> GetKanbanAsync(
        Guid companyId, Guid vacancyId, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<GetRecruitmentKanbanResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/vacancies/{vacancyId}/kanban", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }

    public async Task<(MoveApplicationStageResponse? Result, string? Error)> MoveApplicationStageAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, Guid newStageId, string? notes = null)
    {
        var request = new MoveApplicationStageRequest(companyId, vacancyId, applicationId, newStageId, notes);
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/move-stage",
            request, HrApiJsonOptions.Default);
        var result = await ApiResponseReader.ReadJsonAsync<MoveApplicationStageResponse>(response, HrApiJsonOptions.Default);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to move candidate to the new stage."));
    }
}
