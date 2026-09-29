namespace HR.Modules.Companies.Contracts;

public static class CompanyProbationSettings
{
    public static IReadOnlyList<int> DefaultCheckpointDays { get; } = [30, 60, 90];
}
