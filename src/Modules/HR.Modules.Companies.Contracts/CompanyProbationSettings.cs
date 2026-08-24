namespace HR.Modules.Companies.Contracts;

/// <summary>
/// PROB-03: canonical default checkpoint schedule shared by <c>CompanySettings.CreateDefault</c>
/// (HR.Modules.Companies.Domain) and <c>CompanyProbationSettingsReader</c>'s no-settings-row
/// fallback — both represent the same confirmed default. Mirrors the
/// <c>CompanySicknessSettings</c>/<c>ICompanySicknessSettingsReader</c> pattern established for
/// SICK-04/05.
/// </summary>
public static class CompanyProbationSettings
{
    public static IReadOnlyList<int> DefaultCheckpointDays { get; } = [30, 60, 90];
}
