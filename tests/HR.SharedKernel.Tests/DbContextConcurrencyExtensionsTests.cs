using Microsoft.EntityFrameworkCore;

namespace HR.SharedKernel.Tests;

// Ticket 2 (optimistic concurrency) base code: the shared "pin original version, increment, save,
// translate the stale save" helper. Exercised here against EF Core InMemory with a tiny aggregate
// implementing IVersionedAggregate — InMemory does honour a mapped .IsConcurrencyToken() and
// raises DbUpdateConcurrencyException when the tracked original value no longer matches the store,
// which is exactly what the helper depends on.
public class DbContextConcurrencyExtensionsTests
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
        => new DbContextOptionsBuilder().UseInMemoryDatabase(dbName).Options;

    private const string ConflictMessage = "Someone else changed this first.";

    [Fact]
    public async Task Increments_Version_On_Successful_Save()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var widget = new Widget { Name = "before" };

        await using (var seed = new WidgetContext(Options(dbName)))
        {
            seed.Add(widget);
            await seed.SaveChangesAsync();
        }

        await using var ctx = new WidgetContext(Options(dbName));
        var tracked = await ctx.Widgets.SingleAsync();
        tracked.Name = "after";

        var result = await ctx.SaveChangesWithConcurrencyAsync(tracked, expectedVersion: 1, ConflictMessage, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, tracked.Version);

        await using var verify = new WidgetContext(Options(dbName));
        var saved = await verify.Widgets.SingleAsync();
        Assert.Equal("after", saved.Name);
        Assert.Equal(2, saved.Version);
    }

    [Fact]
    public async Task Returns_Concurrency_Error_When_A_Competing_Update_Bumped_The_Row_First()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var widget = new Widget { Name = "original" };

        await using (var seed = new WidgetContext(Options(dbName)))
        {
            seed.Add(widget);
            await seed.SaveChangesAsync();
        }

        // Context A loads the row at Version 1.
        await using var ctxA = new WidgetContext(Options(dbName));
        var trackedA = await ctxA.Widgets.SingleAsync();
        trackedA.Name = "A wins?";

        // Context B commits first, moving the store to Version 2.
        await using (var ctxB = new WidgetContext(Options(dbName)))
        {
            var trackedB = await ctxB.Widgets.SingleAsync();
            trackedB.Name = "B wins";
            var bResult = await ctxB.SaveChangesWithConcurrencyAsync(trackedB, expectedVersion: 1, ConflictMessage, CancellationToken.None);
            Assert.True(bResult.IsSuccess);
        }

        // Context A now saves with a stale expected version.
        var result = await ctxA.SaveChangesWithConcurrencyAsync(trackedA, expectedVersion: 1, ConflictMessage, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Equal(ConflictMessage, result.Error.Message);

        await using var verify = new WidgetContext(Options(dbName));
        var saved = await verify.Widgets.SingleAsync();
        Assert.Equal("B wins", saved.Name);
        Assert.Equal(2, saved.Version);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_Error()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var widget = new Widget { Name = "original" };

        await using (var seed = new WidgetContext(Options(dbName)))
        {
            seed.Add(widget);
            await seed.SaveChangesAsync();
        }

        await using var ctxA = new WidgetContext(Options(dbName));
        var trackedA = await ctxA.Widgets.SingleAsync();
        trackedA.Name = "late writer";

        var result = await ctxA.SaveChangesWithConcurrencyAsync(trackedA, expectedVersion: null, ConflictMessage, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new WidgetContext(Options(dbName));
        Assert.Equal("original", (await verify.Widgets.SingleAsync()).Name);
    }

    [Fact]
    public async Task ForceSaveChangesAdvancingVersionAsync_Saves_And_Advances_Version()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var widget = new Widget { Name = "before" };

        await using (var seed = new WidgetContext(Options(dbName)))
        {
            seed.Add(widget);
            await seed.SaveChangesAsync();
        }

        await using var ctx = new WidgetContext(Options(dbName));
        var tracked = await ctx.Widgets.SingleAsync();
        tracked.Name = "after";

        var result = await ctx.ForceSaveChangesAdvancingVersionAsync(tracked, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, tracked.Version);

        await using var verify = new WidgetContext(Options(dbName));
        var saved = await verify.Widgets.SingleAsync();
        Assert.Equal("after", saved.Name);
        Assert.Equal(2, saved.Version);
    }

    [Fact]
    public async Task Null_Arguments_Throw()
    {
        await using var ctx = new WidgetContext(Options(Guid.NewGuid().ToString("N")));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DbContextConcurrencyExtensions.SaveChangesWithConcurrencyAsync<Widget>(null!, new Widget(), 1, ConflictMessage, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ctx.SaveChangesWithConcurrencyAsync<Widget>(null!, 1, ConflictMessage, CancellationToken.None));
    }
}
