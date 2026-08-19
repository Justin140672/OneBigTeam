using HR.Modules.Employees.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Employees.Tests;

public class EffectiveNoticePeriodResolverTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task ResolveAsync_Uses_Employee_Override_When_Both_Unit_And_Length_Are_Set()
    {
        var settingsReader = new FakeCompanyNoticePeriodSettingsReader(NoticePeriodUnit.Months, 3);
        var resolver = new EffectiveNoticePeriodResolver(settingsReader);

        var result = await resolver.ResolveAsync(
            CompanyId,
            employeeUnitOverride: NoticePeriodUnit.Weeks,
            employeeLengthOverride: 2,
            positionProfileUnitOverride: NoticePeriodUnit.Months,
            positionProfileLengthOverride: 6,
            CancellationToken.None);

        Assert.Equal(NoticePeriodUnit.Weeks, result.Unit);
        Assert.Equal(2, result.Length);
        Assert.Equal(NoticePeriodSource.Employee, result.Source);
        Assert.Equal(0, settingsReader.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_Falls_Back_To_PositionProfile_Override_When_No_Employee_Override()
    {
        var settingsReader = new FakeCompanyNoticePeriodSettingsReader(NoticePeriodUnit.Months, 3);
        var resolver = new EffectiveNoticePeriodResolver(settingsReader);

        var result = await resolver.ResolveAsync(
            CompanyId,
            employeeUnitOverride: null,
            employeeLengthOverride: null,
            positionProfileUnitOverride: NoticePeriodUnit.Weeks,
            positionProfileLengthOverride: 4,
            CancellationToken.None);

        Assert.Equal(NoticePeriodUnit.Weeks, result.Unit);
        Assert.Equal(4, result.Length);
        Assert.Equal(NoticePeriodSource.PositionProfile, result.Source);
        Assert.Equal(0, settingsReader.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_Falls_Back_To_Company_Default_When_No_Overrides_Present()
    {
        var settingsReader = new FakeCompanyNoticePeriodSettingsReader(NoticePeriodUnit.Months, 1);
        var resolver = new EffectiveNoticePeriodResolver(settingsReader);

        var result = await resolver.ResolveAsync(
            CompanyId,
            employeeUnitOverride: null,
            employeeLengthOverride: null,
            positionProfileUnitOverride: null,
            positionProfileLengthOverride: null,
            CancellationToken.None);

        Assert.Equal(NoticePeriodUnit.Months, result.Unit);
        Assert.Equal(1, result.Length);
        Assert.Equal(NoticePeriodSource.CompanyDefault, result.Source);
        Assert.Equal(1, settingsReader.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_Ignores_Employee_Unit_Set_Without_Length_And_Falls_Through()
    {
        var settingsReader = new FakeCompanyNoticePeriodSettingsReader(NoticePeriodUnit.Months, 1);
        var resolver = new EffectiveNoticePeriodResolver(settingsReader);

        var result = await resolver.ResolveAsync(
            CompanyId,
            employeeUnitOverride: NoticePeriodUnit.Weeks,
            employeeLengthOverride: null,
            positionProfileUnitOverride: NoticePeriodUnit.Months,
            positionProfileLengthOverride: 6,
            CancellationToken.None);

        Assert.Equal(NoticePeriodUnit.Months, result.Unit);
        Assert.Equal(6, result.Length);
        Assert.Equal(NoticePeriodSource.PositionProfile, result.Source);
    }

    [Fact]
    public async Task ResolveAsync_Ignores_Employee_Length_Set_Without_Unit_And_Falls_Through()
    {
        var settingsReader = new FakeCompanyNoticePeriodSettingsReader(NoticePeriodUnit.Months, 1);
        var resolver = new EffectiveNoticePeriodResolver(settingsReader);

        var result = await resolver.ResolveAsync(
            CompanyId,
            employeeUnitOverride: null,
            employeeLengthOverride: 5,
            positionProfileUnitOverride: NoticePeriodUnit.Months,
            positionProfileLengthOverride: 6,
            CancellationToken.None);

        Assert.Equal(NoticePeriodUnit.Months, result.Unit);
        Assert.Equal(6, result.Length);
        Assert.Equal(NoticePeriodSource.PositionProfile, result.Source);
    }

    [Fact]
    public async Task ResolveAsync_Ignores_PositionProfile_Unit_Set_Without_Length_And_Falls_Through_To_Company_Default()
    {
        var settingsReader = new FakeCompanyNoticePeriodSettingsReader(NoticePeriodUnit.Weeks, 2);
        var resolver = new EffectiveNoticePeriodResolver(settingsReader);

        var result = await resolver.ResolveAsync(
            CompanyId,
            employeeUnitOverride: null,
            employeeLengthOverride: null,
            positionProfileUnitOverride: NoticePeriodUnit.Months,
            positionProfileLengthOverride: null,
            CancellationToken.None);

        Assert.Equal(NoticePeriodUnit.Weeks, result.Unit);
        Assert.Equal(2, result.Length);
        Assert.Equal(NoticePeriodSource.CompanyDefault, result.Source);
        Assert.Equal(1, settingsReader.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_Ignores_PositionProfile_Length_Set_Without_Unit_And_Falls_Through_To_Company_Default()
    {
        var settingsReader = new FakeCompanyNoticePeriodSettingsReader(NoticePeriodUnit.Weeks, 2);
        var resolver = new EffectiveNoticePeriodResolver(settingsReader);

        var result = await resolver.ResolveAsync(
            CompanyId,
            employeeUnitOverride: null,
            employeeLengthOverride: null,
            positionProfileUnitOverride: null,
            positionProfileLengthOverride: 8,
            CancellationToken.None);

        Assert.Equal(NoticePeriodUnit.Weeks, result.Unit);
        Assert.Equal(2, result.Length);
        Assert.Equal(NoticePeriodSource.CompanyDefault, result.Source);
        Assert.Equal(1, settingsReader.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_Employee_Override_Takes_Priority_Over_PositionProfile_Override_When_Both_Fully_Set()
    {
        var settingsReader = new FakeCompanyNoticePeriodSettingsReader(NoticePeriodUnit.Months, 1);
        var resolver = new EffectiveNoticePeriodResolver(settingsReader);

        var result = await resolver.ResolveAsync(
            CompanyId,
            employeeUnitOverride: NoticePeriodUnit.Weeks,
            employeeLengthOverride: 3,
            positionProfileUnitOverride: NoticePeriodUnit.Months,
            positionProfileLengthOverride: 12,
            CancellationToken.None);

        Assert.Equal(NoticePeriodSource.Employee, result.Source);
        Assert.Equal(NoticePeriodUnit.Weeks, result.Unit);
        Assert.Equal(3, result.Length);
    }

    private sealed class FakeCompanyNoticePeriodSettingsReader(NoticePeriodUnit unit, int length) : ICompanyNoticePeriodSettingsReader
    {
        public int CallCount { get; private set; }

        public Task<(NoticePeriodUnit Unit, int Length)> GetDefaultNoticePeriodAsync(Guid companyId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult((unit, length));
        }
    }
}
