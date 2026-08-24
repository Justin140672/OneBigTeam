namespace HR.Modules.Companies.Contracts;

public interface ICompanyProbationSettingsReader
{
    Task<int> GetProbationMonthsAsync(Guid companyId, CancellationToken cancellationToken);

    /// <summary>
    /// PROB-03: the company's configured probation review checkpoint days, measured as an offset
    /// in days from the probation start date (e.g. 30 = 30 days after the start date). Always
    /// returns a distinct, ascending-sorted list of positive day offsets. Falls back to the
    /// documented default schedule of [30, 60, 90] when the company has no persisted settings row
    /// or has not configured any checkpoints. See <c>CompanyProbationSettings.DefaultCheckpointDays</c>
    /// for the canonical default.
    /// </summary>
    Task<IReadOnlyList<int>> GetCheckpointDaysAsync(Guid companyId, CancellationToken cancellationToken);
}
