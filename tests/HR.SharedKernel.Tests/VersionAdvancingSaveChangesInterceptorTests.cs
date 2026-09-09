using Microsoft.EntityFrameworkCore;

namespace HR.SharedKernel.Tests;

// Ticket 2 (optimistic concurrency): the centralised version-advance interceptor. Any change to a
// versioned aggregate must bump Version even when the writer does not go through
// SaveChangesWithConcurrencyAsync; and it must never double-bump when the helper already did.
public class VersionAdvancingSaveChangesInterceptorTests
{
    private sealed class Widget : IVersionedAggregate
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public int Version { get; private set; } = 1;
        public void IncrementVersion() => Version++;
    }

    private sealed class WidgetContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var widget = modelBuilder.Entity<Widget>();
            widget.HasKey(w => w.Id);
            widget.Property(w => w.Version).IsConcurrencyToken();
        }
    }

    private static DbContextOptions Options(string dbName)
        => new DbContextOptionsBuilder()
            .UseInMemoryDatabase(dbName)
            .UseVersionedAggregates()
            .Options;

    [Fact]
    public async Task Plain_SaveChanges_On_A_Modified_Versioned_Entity_Advances_Version()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var widget = new Widget { Name = "before" };

        await using (var seed = new WidgetContext(Options(dbName)))
        {
            seed.Add(widget);
            await seed.SaveChangesAsync();
        }

        await using (var ctx = new WidgetContext(Options(dbName)))
        {
            var tracked = await ctx.Widgets.SingleAsync();
            tracked.Name = "after";
            await ctx.SaveChangesAsync();
        }

        await using var verify = new WidgetContext(Options(dbName));
        var saved = await verify.Widgets.SingleAsync();
        Assert.Equal("after", saved.Name);
        Assert.Equal(2, saved.Version);
    }

    [Fact]
    public async Task Does_Not_Double_Increment_When_Save_Helper_Already_Bumped()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var widget = new Widget { Name = "before" };

        await using (var seed = new WidgetContext(Options(dbName)))
        {
            seed.Add(widget);
            await seed.SaveChangesAsync();
        }

        await using (var ctx = new WidgetContext(Options(dbName)))
        {
            var tracked = await ctx.Widgets.SingleAsync();
            tracked.Name = "after";
            var result = await ctx.SaveChangesWithConcurrencyAsync(tracked, expectedVersion: 1, "conflict", CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await using var verify = new WidgetContext(Options(dbName));
        Assert.Equal(2, (await verify.Widgets.SingleAsync()).Version);
    }

    [Fact]
    public async Task Unchanged_Entity_Is_Not_Bumped()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var widget = new Widget { Name = "before" };

        await using (var seed = new WidgetContext(Options(dbName)))
        {
            seed.Add(widget);
            await seed.SaveChangesAsync();
        }

        await using (var ctx = new WidgetContext(Options(dbName)))
        {
            _ = await ctx.Widgets.SingleAsync();
            await ctx.SaveChangesAsync();
        }

        await using var verify = new WidgetContext(Options(dbName));
        Assert.Equal(1, (await verify.Widgets.SingleAsync()).Version);
    }
}
