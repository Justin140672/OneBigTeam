using HR.Modules.Employees.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees.Tests;

public class ProbationDateResolverTests
{
    private static readonly DateOnly StartDate = new(2026, 7, 1);
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task ResolveEndDateAsync_Uses_PositionProfile_Override_When_Present()
    {
        var settingsReader = new FakeCompanySettingsReader(companyMonths: 12);
        var resolver = new ProbationDateResolver(settingsReader);

        var result = await resolver.ResolveEndDateAsync(CompanyId, 3, StartDate, CancellationToken.None);

        Assert.Equal(StartDate.AddMonths(3), result);
        Assert.Equal(0, settingsReader.CallCount);
    }

    [Fact]
    public async Task ResolveEndDateAsync_Falls_Back_To_Company_Default_When_No_Override()
    {
        var settingsReader = new FakeCompanySettingsReader(companyMonths: 9);
        var resolver = new ProbationDateResolver(settingsReader);

        var result = await resolver.ResolveEndDateAsync(CompanyId, null, StartDate, CancellationToken.None);

        Assert.Equal(StartDate.AddMonths(9), result);
        Assert.Equal(1, settingsReader.CallCount);
    }

    [Fact]
    public async Task ResolveEndDateAsync_Calculates_End_Date_As_StartDate_Plus_Months()
    {
        var settingsReader = new FakeCompanySettingsReader(companyMonths: 6);
        var resolver = new ProbationDateResolver(settingsReader);

        var result = await resolver.ResolveEndDateAsync(CompanyId, null, new DateOnly(2026, 1, 1), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 7, 1), result);
    }

    [Fact]
    public async Task ResolveEndDateAsync_Does_Not_Call_Company_Settings_When_Override_Provided()
    {
        var settingsReader = new FakeCompanySettingsReader(companyMonths: 6);
        var resolver = new ProbationDateResolver(settingsReader);

        await resolver.ResolveEndDateAsync(CompanyId, 1, StartDate, CancellationToken.None);

        Assert.Equal(0, settingsReader.CallCount);
    }

    private sealed class FakeCompanySettingsReader(int companyMonths) : ICompanyProbationSettingsReader
    {
        public int CallCount { get; private set; }

        public Task<int> GetProbationMonthsAsync(Guid companyId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(companyMonths);
        }

        public Task<IReadOnlyList<int>> GetCheckpointDaysAsync(Guid companyId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<int>>([30, 60, 90]);
    }
}
