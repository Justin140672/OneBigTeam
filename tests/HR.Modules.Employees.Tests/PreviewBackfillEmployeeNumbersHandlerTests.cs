using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.PreviewBackfillEmployeeNumbers;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class PreviewBackfillEmployeeNumbersHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static PreviewBackfillEmployeeNumbersHandler BuildHandler(
        EmployeesDbContext context,
        SpyCompanyEmployeeNumberSettingsReader? settingsReader = null) =>
        new(context, settingsReader ?? new SpyCompanyEmployeeNumberSettingsReader());

    private static Employee CreateEmployee(
        Guid companyId, string firstName, string lastName, DateOnly startDate, string employeeNumber, DateTimeOffset now) =>
        Employee.Create(
            Guid.NewGuid(), companyId, firstName, lastName, $"{firstName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
            startDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            employeeNumber, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Company_Is_Not_In_Automatic_Mode()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var handler = BuildHandler(
            context, new SpyCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Manual));

        var result = await handler.HandleAsync(
            new PreviewBackfillEmployeeNumbersRequest(companyId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_Candidates_When_No_Employees_Are_Missing_A_Number()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        context.Employees.Add(CreateEmployee(companyId, "Alice", "Smith", new DateOnly(2024, 1, 1), "EMP-0001", now));
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new SpyCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Automatic));

        var result = await handler.HandleAsync(
            new PreviewBackfillEmployeeNumbersRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Candidates);
    }

    [Fact]
    public async Task HandleAsync_Orders_Candidates_By_StartDate_Then_LastName_Then_FirstName()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        // Deliberately inserted out of expected order.
        var later = CreateEmployee(companyId, "Zoe", "Adams", new DateOnly(2024, 3, 1), "", now);
        var earlierByLastName = CreateEmployee(companyId, "Bob", "Zephyr", new DateOnly(2024, 1, 1), "", now);
        var earlierByFirstName = CreateEmployee(companyId, "Alice", "Zephyr", new DateOnly(2024, 1, 1), "", now);
        context.Employees.AddRange(later, earlierByLastName, earlierByFirstName);
        await context.SaveChangesAsync();

        var settingsReader = new SpyCompanyEmployeeNumberSettingsReader(
            EmployeeNumberMode.Automatic, new EmployeeNumberSequencePreview("EMP-", 1, 3));
        var handler = BuildHandler(context, settingsReader);

        var result = await handler.HandleAsync(
            new PreviewBackfillEmployeeNumbersRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var candidates = result.Value!.Candidates;
        Assert.Equal(3, candidates.Count);
        Assert.Equal(earlierByFirstName.Id, candidates[0].EmployeeId);
        Assert.Equal(earlierByLastName.Id, candidates[1].EmployeeId);
        Assert.Equal(later.Id, candidates[2].EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Computes_Predicted_Numbers_From_Sequence_Preview_Without_Mutating_State()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var first = CreateEmployee(companyId, "Alice", "Smith", new DateOnly(2024, 1, 1), "", now);
        var second = CreateEmployee(companyId, "Bob", "Jones", new DateOnly(2024, 2, 1), "", now);
        context.Employees.AddRange(first, second);
        await context.SaveChangesAsync();

        var settingsReader = new SpyCompanyEmployeeNumberSettingsReader(
            EmployeeNumberMode.Automatic, new EmployeeNumberSequencePreview("EMP-", 125, 5));
        var handler = BuildHandler(context, settingsReader);

        var result = await handler.HandleAsync(
            new PreviewBackfillEmployeeNumbersRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var candidates = result.Value!.Candidates;
        Assert.Equal("EMP-00125", candidates[0].PredictedEmployeeNumber);
        Assert.Equal("EMP-00126", candidates[1].PredictedEmployeeNumber);

        // GetSequencePreviewAsync (a read-only projection) is the only thing consulted — the real
        // atomic counter (GenerateNextAsync on IEmployeeNumberGenerator) is never called by preview.
        Assert.Equal(1, settingsReader.GetSequencePreviewCallCount);
    }

    [Fact]
    public async Task HandleAsync_Only_Includes_Employees_Scoped_To_The_Requested_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var ownEmployee = CreateEmployee(companyId, "Alice", "Smith", new DateOnly(2024, 1, 1), "", now);
        var otherEmployee = CreateEmployee(otherCompanyId, "Bob", "Jones", new DateOnly(2024, 1, 1), "", now);
        context.Employees.AddRange(ownEmployee, otherEmployee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new SpyCompanyEmployeeNumberSettingsReader(EmployeeNumberMode.Automatic));

        var result = await handler.HandleAsync(
            new PreviewBackfillEmployeeNumbersRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var candidate = Assert.Single(result.Value!.Candidates);
        Assert.Equal(ownEmployee.Id, candidate.EmployeeId);
    }
}

/// <summary>
/// Spy over <see cref="FakeCompanyEmployeeNumberSettingsReader"/>'s behaviour, additionally
/// counting GetSequencePreviewAsync calls so tests can assert the preview handler never mutates
/// state via a second read (and never consults the real generator at all).
/// </summary>
internal sealed class SpyCompanyEmployeeNumberSettingsReader(
    EmployeeNumberMode mode = EmployeeNumberMode.Manual,
    EmployeeNumberSequencePreview? sequencePreview = null)
    : ICompanyEmployeeNumberSettingsReader
{
    public int GetSequencePreviewCallCount { get; private set; }

    public Task<EmployeeNumberMode> GetModeAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(mode);

    public Task<EmployeeNumberSequencePreview> GetSequencePreviewAsync(Guid companyId, CancellationToken cancellationToken)
    {
        GetSequencePreviewCallCount++;
        return Task.FromResult(sequencePreview ?? new EmployeeNumberSequencePreview(null, 1, 1));
    }
}
