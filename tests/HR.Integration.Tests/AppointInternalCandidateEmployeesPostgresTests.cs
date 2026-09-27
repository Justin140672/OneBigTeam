using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static HR.Integration.Tests.Infrastructure.InternalAppointmentTestSeeder;

namespace HR.Integration.Tests;

/// <summary>
/// Internal recruitment Ticket 7: real-PostgreSQL coverage of <see cref="IEmployeeInternalAppointmentService"/>
/// (the shared Testcontainers database, fully migrated — including
/// InternalRecruitment07_AddPromotionAppointmentFields). EF InMemory has no transactions and ignores
/// indexes, so only Postgres can prove:
/// <list type="bullet">
/// <item>two concurrent AppointAsync calls with the same source reference record exactly ONE promotion
/// and ONE compensation row (the loser's compensation insert is rolled back with its transaction and it
/// resumes the winner's change), and both callers get the same PromotionId;</item>
/// <item>the filtered unique index ix_employee_promotions_company_id_source_reference rejects a
/// duplicate source reference, while ordinary promotions (null source reference) are unaffected.</item>
/// </list>
/// Each round/test seeds its own company. Assertions are on outcomes and final state, never on which
/// call won.
/// </summary>
[Collection("Integration")]
public class AppointInternalCandidateEmployeesPostgresTests
{
    private const int Rounds = 5;
    private readonly ApiWebApplicationFactory _factory;

    public AppointInternalCandidateEmployeesPostgresTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static InternalAppointmentRequest Request(EmployeeWorld world, string sourceReference) =>
        new(
            world.CompanyId,
            world.EmployeeId,
            world.Target.PositionProfileId,
            Today,
            world.NewManagerId,
            sourceReference,
            "Internal appointment: Engineering Manager",
            Guid.NewGuid(),
            ConfirmBackdatedEffectiveDate: false,
            new InternalAppointmentCompensation("Annual", 78000m, "GBP", 37.5m, 1m, null));

    [Fact]
    public async Task Concurrent_Appoints_With_Same_Source_Reference_Record_One_Promotion_And_One_Compensation()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var companyId = Guid.NewGuid();
            var world = await SeedEmployeesAsync(_factory, companyId);
            var sourceReference = $"recruitment:application:{Guid.NewGuid()}";

            async Task<HR.SharedKernel.Result<InternalAppointmentResult>> AppointInOwnScopeAsync()
            {
                using var scope = _factory.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IEmployeeInternalAppointmentService>();
                return await service.AppointAsync(Request(world, sourceReference), CancellationToken.None);
            }

            var results = await Task.WhenAll(
                Task.Run(AppointInOwnScopeAsync),
                Task.Run(AppointInOwnScopeAsync));

            Assert.All(results, r => Assert.True(r.IsSuccess, r.IsFailure ? r.Error.Message : null));
            Assert.Equal(results[0].Value!.PromotionId, results[1].Value!.PromotionId);
            Assert.Equal(results[0].Value!.CompensationId, results[1].Value!.CompensationId);
            Assert.Single(results, r => !r.Value!.WasAlreadyRecorded);

            using var verify = _factory.Services.CreateScope();
            var db = verify.ServiceProvider.GetRequiredService<EmployeesDbContext>();

            var promotion = Assert.Single(await db.EmployeePromotions.AsNoTracking()
                .Where(p => p.CompanyId == companyId && p.EmployeeId == world.EmployeeId)
                .ToListAsync());
            Assert.Equal(results[0].Value!.PromotionId, promotion.Id);
            Assert.Equal(sourceReference, promotion.SourceReference);
            Assert.NotNull(promotion.CompletedAt);

            var compensation = Assert.Single(await db.Compensations.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.EmployeeId == world.EmployeeId)
                .ToListAsync());
            Assert.Equal(promotion.CompensationId, compensation.Id);
            Assert.Equal(CompensationChangeReason.RoleChange, compensation.Reason);

            var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == world.EmployeeId);
            Assert.Equal(world.Target.PositionProfileId, employee.PositionProfileId);
            Assert.Equal(world.Target.DepartmentId, employee.DepartmentId);
            Assert.Equal(world.Target.LocationId, employee.LocationId);
            Assert.Equal(world.NewManagerId, employee.ManagerId);
            Assert.Equal(world.EmployeeNumber, employee.EmployeeNumber);
        }
    }

    [Fact]
    public async Task Sequential_Retry_With_Same_Source_Reference_Returns_The_Recorded_Change()
    {
        var companyId = Guid.NewGuid();
        var world = await SeedEmployeesAsync(_factory, companyId);
        var sourceReference = $"recruitment:application:{Guid.NewGuid()}";

        InternalAppointmentResult first, second;
        using (var scope = _factory.Services.CreateScope())
            first = (await scope.ServiceProvider.GetRequiredService<IEmployeeInternalAppointmentService>()
                .AppointAsync(Request(world, sourceReference), CancellationToken.None)).Value!;
        using (var scope = _factory.Services.CreateScope())
            second = (await scope.ServiceProvider.GetRequiredService<IEmployeeInternalAppointmentService>()
                .AppointAsync(Request(world, sourceReference), CancellationToken.None)).Value!;

        Assert.Equal(first.PromotionId, second.PromotionId);
        Assert.Equal(first.CompensationId, second.CompensationId);
        Assert.False(first.WasAlreadyRecorded);
        Assert.True(second.WasAlreadyRecorded);

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        Assert.Equal(1, await db.EmployeePromotions.CountAsync(p => p.EmployeeId == world.EmployeeId));
        Assert.Equal(1, await db.Compensations.CountAsync(c => c.EmployeeId == world.EmployeeId));
    }

    [Fact]
    public async Task Unique_Index_Rejects_A_Duplicate_Source_Reference_In_The_Same_Company()
    {
        var companyId = Guid.NewGuid();
        var world = await SeedEmployeesAsync(_factory, companyId);
        var sourceReference = $"recruitment:application:{Guid.NewGuid()}";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            db.EmployeePromotions.Add(NewPromotion(world, sourceReference));
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            db.EmployeePromotions.Add(NewPromotion(world, sourceReference));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.Equal("ix_employee_promotions_company_id_source_reference", postgres.ConstraintName);
        }
    }

    [Fact]
    public async Task Unique_Index_Allows_Many_Promotions_Without_A_Source_Reference()
    {
        var companyId = Guid.NewGuid();
        var world = await SeedEmployeesAsync(_factory, companyId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        db.EmployeePromotions.AddRange(NewPromotion(world, sourceReference: null), NewPromotion(world, sourceReference: null));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.EmployeePromotions.CountAsync(p => p.EmployeeId == world.EmployeeId));
    }

    [Fact]
    public async Task Unique_Index_Is_Scoped_Per_Company()
    {
        var worldA = await SeedEmployeesAsync(_factory, Guid.NewGuid());
        var worldB = await SeedEmployeesAsync(_factory, Guid.NewGuid());
        var sourceReference = $"recruitment:application:{Guid.NewGuid()}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        db.EmployeePromotions.AddRange(NewPromotion(worldA, sourceReference), NewPromotion(worldB, sourceReference));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.EmployeePromotions.CountAsync(p => p.SourceReference == sourceReference));
    }

    private static EmployeePromotion NewPromotion(EmployeeWorld world, string? sourceReference) =>
        EmployeePromotion.Create(
            Guid.NewGuid(), world.CompanyId, world.EmployeeId,
            world.Current.PositionProfileId, world.Target.PositionProfileId,
            newManagerId: null, world.Target.LocationId, Today.AddDays(30), "Reason.", notes: null,
            compensationId: null, Guid.NewGuid(), DateTimeOffset.UtcNow,
            newDepartmentId: world.Target.DepartmentId, sourceReference: sourceReference);
}
