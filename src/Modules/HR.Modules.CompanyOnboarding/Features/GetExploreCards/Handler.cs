using HR.SharedKernel;

namespace HR.Modules.CompanyOnboarding.Features.GetExploreCards;

internal sealed class GetExploreCardsHandler
{
    // Static, hardcoded card metadata for Phase A — no module dependency needed since these are
    // just navigational links into other modules' existing top-level pages. LinkUrl contains a
    // "{companyId}" placeholder since HR.Web's routes are company-scoped
    // (e.g. "/companies/{CompanyId:guid}/employees") — HR.Web substitutes the current company id
    // in before rendering the actual href.
    private static readonly IReadOnlyList<ExploreCardResponse> Cards =
    [
        new("Employees", "Manage your workforce", "/companies/{companyId}/employees", false),
        new("Leave", "Track time off and leave balances", "/companies/{companyId}/leave-policies", false),
        new("Recruitment", "Hire and onboard new talent", "/companies/{companyId}/vacancies", false),
        new("Documents", "Store and share company documents", "/companies/{companyId}/shared-documents", false),
        new("Reports", "Reporting and analytics", "/companies/{companyId}/reporting", true),
        new("Company Settings", "Configure your company profile and HR policies", "/companies/{companyId}/edit", false),
    ];

    public Task<Result<GetExploreCardsResponse>> HandleAsync(
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(new GetExploreCardsResponse(Cards)));
    }
}
