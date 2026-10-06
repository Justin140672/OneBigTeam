using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class RespondToInternalOfferConcurrencyTests(RecruitmentDatabaseFixture fixture)
    : IClassFixture<RecruitmentDatabaseFixture>
{
    private const int Rounds = 5;

    [Fact]
    public async Task Concurrent_Identical_Accepts_Record_Exactly_One_Response()
    {
        for (var round = 0; round < Rounds; round++)
        {
            await using var seedDb = fixture.BuildContext();
            var h = await InternalOfferHarness.CreateAsync(seedDb);
            await h.OfferAsync();

            await using var dbA = fixture.BuildContext();
            await using var dbB = fixture.BuildContext();
            var wiringA = Wire(h, dbA);
            var wiringB = Wire(h, dbB);

            var results = await Task.WhenAll(
                Task.Run(() => h.RespondHandler(dbA, wiringA).HandleAsync(h.Respond("Accept"), h.EmployeeId, CancellationToken.None)),
                Task.Run(() => h.RespondHandler(dbB, wiringB).HandleAsync(h.Respond("Accept"), h.EmployeeId, CancellationToken.None)));

            Assert.All(results, r => Assert.True(r.IsSuccess, r.IsFailure ? r.Error.Message : null));
            Assert.Equal(1, results.Count(r => !r.Value!.WasAlreadyRecorded));

            await using var verifyDb = fixture.BuildContext();
            var saved = await verifyDb.Applications.SingleAsync(a => a.Id == h.Application.Id);
            Assert.Equal(OfferResponseStatus.Accepted, saved.OfferResponseStatus);
            Assert.Equal(h.EmployeeId, saved.OfferRespondedByUserId);
        }
    }

    [Fact]
    public async Task Concurrent_Accept_And_Decline_Produce_Exactly_One_Winner()
    {
        for (var round = 0; round < Rounds; round++)
        {
            await using var seedDb = fixture.BuildContext();
            var h = await InternalOfferHarness.CreateAsync(seedDb);
            await h.OfferAsync();

            await using var dbA = fixture.BuildContext();
            await using var dbB = fixture.BuildContext();

            var results = await Task.WhenAll(
                Task.Run(() => h.RespondHandler(dbA, Wire(h, dbA)).HandleAsync(h.Respond("Accept"), h.EmployeeId, CancellationToken.None)),
                Task.Run(() => h.RespondHandler(dbB, Wire(h, dbB)).HandleAsync(h.Respond("Decline"), h.EmployeeId, CancellationToken.None)));

            Assert.Equal(1, results.Count(r => r.IsSuccess));
            Assert.Equal(1, results.Count(r => r.IsFailure));
            Assert.Contains(results.Single(r => r.IsFailure).Error.Code, new[] { "conflict", "concurrency" });

            await using var verifyDb = fixture.BuildContext();
            var saved = await verifyDb.Applications.SingleAsync(a => a.Id == h.Application.Id);
            Assert.NotEqual(OfferResponseStatus.AwaitingResponse, saved.OfferResponseStatus);
        }
    }

    private static InternalOfferWiring Wire(InternalOfferHarness h, HR.Modules.Recruitment.Persistence.RecruitmentDbContext db)
    {
        var wiring = InternalOfferWiring.For(db, h.Clock, h.Wiring.Positions);
        wiring.Applicants.Set(FakeEmployeeApplicantReader.Profile(h.CompanyId, h.EmployeeId));
        return wiring;
    }
}
