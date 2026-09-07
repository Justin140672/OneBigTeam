using HR.Modules.Marketing.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Tests.Infrastructure;

internal static class MarketingTestContext
{
    public static MarketingDbContext Build() =>
        new(new DbContextOptionsBuilder<MarketingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
