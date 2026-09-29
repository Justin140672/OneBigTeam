namespace HR.Web.Services;

public sealed record OnboardingTaskAction(
    string Label,
    string Href,
    bool IsPrimary,
    bool IsDownload,
    string? IconCss = null,
    string? AccessibleName = null);

public static class OnboardingTaskActionPresenter
{
    public const string ImportEmployeesKey = "import-employees";
    public const string ReturnUrlSuffix = "returnUrl=%2Fgetting-started";

    public static IReadOnlyList<OnboardingTaskAction> Present(string key, string linkUrl, Guid companyId, bool isCompleted)
    {
        if (isCompleted)
            return [];

        var href = WithReturnUrl(linkUrl.Replace("{companyId}", companyId.ToString()));

        if (key == ImportEmployeesKey)
        {
            return
            [
                new OnboardingTaskAction("Add or import employees", href, IsPrimary: true, IsDownload: false),
                new OnboardingTaskAction(
                    "Download import template",
                    $"/companies/{companyId}/data-import/employees/template/download",
                    IsPrimary: false,
                    IsDownload: true,
                    IconCss: "fa-solid fa-download",
                    AccessibleName: "Download import template (spreadsheet file)"),
            ];
        }

        return [new OnboardingTaskAction("Go to task", href, IsPrimary: true, IsDownload: false)];
    }

    private static string WithReturnUrl(string url) =>
        $"{url}{(url.Contains('?') ? "&" : "?")}{ReturnUrlSuffix}";
}
