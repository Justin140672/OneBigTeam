using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Tests;

public class EmployeePromotionTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 24, 10, 0, 0, TimeSpan.Zero);

    private static EmployeePromotion CreatePending(DateTimeOffset now, DateOnly? effectiveDate = null) =>
        EmployeePromotion.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(),
            newManagerId: null, newLocationId: null,
            effectiveDate ?? new DateOnly(2026, 8, 1),
            "Promoted for excellent performance.", notes: null,
            compensationId: null, Guid.NewGuid(), now);

    [Fact]
    public void Create_Sets_All_Properties_And_Leaves_CompletedAt_Null()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var previousPositionProfileId = Guid.NewGuid();
        var newPositionProfileId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        var newLocationId = Guid.NewGuid();
        var compensationId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var effectiveDate = new DateOnly(2026, 8, 1);

        var promotion = EmployeePromotion.Create(
            id, companyId, employeeId, previousPositionProfileId, newPositionProfileId,
            newManagerId, newLocationId, effectiveDate, "Promotion reason.", "Some notes.",
            compensationId, createdBy, FixedNow);

        Assert.Equal(id, promotion.Id);
        Assert.Equal(companyId, promotion.CompanyId);
        Assert.Equal(employeeId, promotion.EmployeeId);
        Assert.Equal(previousPositionProfileId, promotion.PreviousPositionProfileId);
        Assert.Equal(newPositionProfileId, promotion.NewPositionProfileId);
        Assert.Equal(newManagerId, promotion.NewManagerId);
        Assert.Equal(newLocationId, promotion.NewLocationId);
        Assert.Equal(effectiveDate, promotion.EffectiveDate);
        Assert.Equal("Promotion reason.", promotion.Reason);
        Assert.Equal("Some notes.", promotion.Notes);
        Assert.Equal(compensationId, promotion.CompensationId);
        Assert.Equal(createdBy, promotion.CreatedBy);
        Assert.Equal(FixedNow, promotion.CreatedDate);
        Assert.Null(promotion.CompletedAt);
    }

    [Fact]
    public void Create_Allows_Null_NewManagerId_NewLocationId_Notes_And_CompensationId()
    {
        var promotion = CreatePending(FixedNow);

        Assert.Null(promotion.NewManagerId);
        Assert.Null(promotion.NewLocationId);
        Assert.Null(promotion.Notes);
        Assert.Null(promotion.CompensationId);
    }

    [Fact]
    public void Complete_Sets_CompletedAt()
    {
        var promotion = CreatePending(FixedNow);
        var completedAt = FixedNow.AddDays(1);

        promotion.Complete(completedAt);

        Assert.Equal(completedAt, promotion.CompletedAt);
    }

    [Fact]
    public void Complete_Called_Twice_Throws()
    {
        var promotion = CreatePending(FixedNow);
        promotion.Complete(FixedNow.AddDays(1));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            promotion.Complete(FixedNow.AddDays(2)));

        Assert.Equal("Cannot complete a promotion that has already been completed.", ex.Message);
    }
}
