using HR.Modules.Support.Domain;
using HR.Modules.Support.Jobs;
using HR.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Support.Tests;

/// <summary>
/// P1 stored-XSS fix: the idempotent backfill that re-sanitises support response bodies stored
/// before write-time sanitisation existed. Legacy rows are simulated by overwriting BodyHtml with
/// raw markup (bypassing SupportResponse.Create's sanitisation) before saving.
/// </summary>
public class SupportResponseBodySanitisationJobTests
{
    private static readonly DateTimeOffset SeedNow = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private const string CleanBody = "<p>All sorted, <strong>thanks</strong>.</p>";

    private static SupportDbContext BuildContext(string databaseName) =>
        new(new DbContextOptionsBuilder<SupportDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static SupportRequest CreateRequest(Guid companyId) =>
        SupportRequest.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), null,
            SupportRequestType.AskQuestion, "Title", "Description", SupportRequestPriority.Low,
            "SUP-1", null, null, null, false, null, null, SeedNow);

    private static SupportResponse CreateLegacyRawResponse(SupportRequest request, string rawBody)
    {
        var response = SupportResponse.Create(
            Guid.NewGuid(), request.Id, request.CompanyId, Guid.NewGuid(), false, "placeholder", SeedNow);
        SupportResponseTests.OverwriteBodyWithRawLegacyValue(response, rawBody);
        return response;
    }

    private static SupportResponse CreateCleanResponse(SupportRequest request) =>
        SupportResponse.Create(Guid.NewGuid(), request.Id, request.CompanyId, Guid.NewGuid(), true, CleanBody, SeedNow);

    private static async Task<int> RunJobAsync(string databaseName)
    {
        // Fresh context per run, as a hosted job would get from its own DI scope.
        await using var db = BuildContext(databaseName);
        var job = new SupportResponseBodySanitisationJob(db, NullLogger<SupportResponseBodySanitisationJob>.Instance);
        return await job.ExecuteAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_Resanitises_Only_Dirty_Rows_Across_Companies_And_Is_Idempotent()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        var dirtyIds = new List<Guid>();
        var cleanIds = new List<Guid>();
        var originalCompany = new Dictionary<Guid, Guid>();

        await using (var seed = BuildContext(databaseName))
        {
            var requestA = CreateRequest(companyA);
            var requestB = CreateRequest(companyB);
            seed.SupportRequests.AddRange(requestA, requestB);

            var rows = new[]
            {
                CreateLegacyRawResponse(requestA, SupportResponseTests.MaliciousBody),
                CreateLegacyRawResponse(requestA, "<p onclick=\"alert(1)\">clicky</p>"),
                CreateLegacyRawResponse(requestB, "<a href=\"JaVaScRiPt:alert(1)\">x</a><svg onload=alert(1)></svg>"),
            };
            var clean = new[] { CreateCleanResponse(requestA), CreateCleanResponse(requestB) };

            seed.SupportResponses.AddRange(rows);
            seed.SupportResponses.AddRange(clean);
            await seed.SaveChangesAsync();

            dirtyIds.AddRange(rows.Select(r => r.Id));
            cleanIds.AddRange(clean.Select(r => r.Id));
            foreach (var r in rows.Concat(clean))
                originalCompany[r.Id] = r.CompanyId;
        }

        // Precondition: the dirty rows really were persisted raw.
        await using (var check = BuildContext(databaseName))
        {
            var stored = await check.SupportResponses.AsNoTracking().SingleAsync(r => r.Id == dirtyIds[0]);
            Assert.Equal(SupportResponseTests.MaliciousBody, stored.BodyHtml);
        }

        var firstRun = await RunJobAsync(databaseName);

        Assert.Equal(dirtyIds.Count, firstRun);

        await using (var verify = BuildContext(databaseName))
        {
            var all = await verify.SupportResponses.AsNoTracking().ToListAsync();
            Assert.Equal(dirtyIds.Count + cleanIds.Count, all.Count);

            foreach (var row in all)
            {
                SupportResponseTests.AssertBodyIsClean(row.BodyHtml);
                Assert.Equal(originalCompany[row.Id], row.CompanyId);
            }

            Assert.All(all.Where(r => cleanIds.Contains(r.Id)), r => Assert.Equal(CleanBody, r.BodyHtml));

            var mixed = all.Single(r => r.Id == dirtyIds[0]);
            Assert.Contains("<strong>there</strong>", mixed.BodyHtml);
            Assert.Equal("<p>clicky</p>", all.Single(r => r.Id == dirtyIds[1]).BodyHtml);
            Assert.Equal("<a>x</a>", all.Single(r => r.Id == dirtyIds[2]).BodyHtml);
        }

        var secondRun = await RunJobAsync(databaseName);

        Assert.Equal(0, secondRun);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Zero_When_There_Are_No_Responses()
    {
        Assert.Equal(0, await RunJobAsync(Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Zero_And_Leaves_Rows_Untouched_When_All_Are_Clean()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using (var seed = BuildContext(databaseName))
        {
            var request = CreateRequest(Guid.NewGuid());
            seed.SupportRequests.Add(request);
            seed.SupportResponses.AddRange(CreateCleanResponse(request), CreateCleanResponse(request));
            await seed.SaveChangesAsync();
        }

        Assert.Equal(0, await RunJobAsync(databaseName));

        await using var verify = BuildContext(databaseName);
        Assert.All(await verify.SupportResponses.AsNoTracking().ToListAsync(), r => Assert.Equal(CleanBody, r.BodyHtml));
    }

    [Fact]
    public async Task ExecuteAsync_Processes_Every_Chunk_When_Rows_Exceed_The_Batch_Size()
    {
        // 2 full batches + a partial third, so an off-by-one in chunking (e.g. only the first
        // batch, or dropping the trailing partial batch) leaves raw rows behind.
        var dirtyCount = SupportResponseBodySanitisationJob.BatchSize * 2 + 50;
        var databaseName = Guid.NewGuid().ToString("N");

        await using (var seed = BuildContext(databaseName))
        {
            var request = CreateRequest(Guid.NewGuid());
            seed.SupportRequests.Add(request);
            for (var i = 0; i < dirtyCount; i++)
                seed.SupportResponses.Add(CreateLegacyRawResponse(request, $"<p>row {i}</p><script>alert({i})</script><img src=x onerror=alert({i})>"));
            await seed.SaveChangesAsync();
        }

        Assert.Equal(dirtyCount, await RunJobAsync(databaseName));

        await using (var verify = BuildContext(databaseName))
        {
            var all = await verify.SupportResponses.AsNoTracking().ToListAsync();
            Assert.Equal(dirtyCount, all.Count);
            Assert.All(all, r =>
            {
                SupportResponseTests.AssertBodyIsClean(r.BodyHtml);
                Assert.StartsWith("<p>row ", r.BodyHtml);
            });
        }

        Assert.Equal(0, await RunJobAsync(databaseName));
    }

    [Fact]
    public async Task ExecuteAsync_Handles_Exactly_One_Full_Batch()
    {
        // Boundary: exactly BatchSize rows is a single full chunk with no remainder.
        var databaseName = Guid.NewGuid().ToString("N");
        await using (var seed = BuildContext(databaseName))
        {
            var request = CreateRequest(Guid.NewGuid());
            seed.SupportRequests.Add(request);
            for (var i = 0; i < SupportResponseBodySanitisationJob.BatchSize; i++)
                seed.SupportResponses.Add(CreateLegacyRawResponse(request, "<p onclick=\"x\">hi</p>"));
            await seed.SaveChangesAsync();
        }

        Assert.Equal(SupportResponseBodySanitisationJob.BatchSize, await RunJobAsync(databaseName));
        Assert.Equal(0, await RunJobAsync(databaseName));
    }
}
