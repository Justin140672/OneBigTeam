using HR.Infrastructure.Abstractions;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeCompanyDefaultDataSeeder : ICompanyDefaultDataSeeder
{
    public int CallCount { get; private set; }

    public List<Guid> SeededCompanyIds { get; } = [];

    public bool ShouldThrow { get; set; }

    public CompanyDefaultDataResult ResultToReturn { get; set; } = new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    public Func<Task>? BeforeEffect { get; set; }

    public async Task<CompanyDefaultDataResult> SeedDefaultsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        CallCount++;
        SeededCompanyIds.Add(companyId);

        if (BeforeEffect is { } before)
        {
            await before();
        }

        if (ShouldThrow)
        {
            throw new InvalidOperationException("Simulated default data seeding failure.");
        }

        return ResultToReturn;
    }
}
