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

    // ---- Internal recruitment Ticket 7 ----

    private static EmployeePromotion CreateWith(
        Guid? newManagerId = null,
        Guid? newDepartmentId = null,
        bool clearsManager = false,
        string? sourceReference = null) =>
        EmployeePromotion.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            newManagerId, newLocationId: null, new DateOnly(2026, 8, 1), "Reason.", notes: null,
            compensationId: null, Guid.NewGuid(), FixedNow,
            newDepartmentId: newDepartmentId, clearsManager: clearsManager, sourceReference: sourceReference);

    [Fact]
    public void Create_Throws_When_Clearing_Manager_And_Assigning_New_Manager()
    {
        Assert.Throws<ArgumentException>(() => CreateWith(newManagerId: Guid.NewGuid(), clearsManager: true));
    }

    [Fact]
    public void Create_Allows_ClearsManager_Without_New_Manager()
    {
        var promotion = CreateWith(clearsManager: true);

        Assert.True(promotion.ClearsManager);
        Assert.Null(promotion.NewManagerId);
    }

    [Fact]
    public void Create_Defaults_New_Fields_For_Ordinary_Promotions()
    {
        var promotion = CreatePending(FixedNow);

        Assert.Null(promotion.NewDepartmentId);
        Assert.False(promotion.ClearsManager);
        Assert.Null(promotion.SourceReference);
        Assert.False(promotion.IsInternalAppointment);
    }

    [Fact]
    public void Create_Records_NewDepartmentId()
    {
        var departmentId = Guid.NewGuid();

        Assert.Equal(departmentId, CreateWith(newDepartmentId: departmentId).NewDepartmentId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Normalises_Blank_SourceReference_To_Null(string sourceReference)
    {
        var promotion = CreateWith(sourceReference: sourceReference);

        Assert.Null(promotion.SourceReference);
        Assert.False(promotion.IsInternalAppointment);
    }

    [Fact]
    public void Create_Trims_SourceReference()
    {
        var applicationId = Guid.NewGuid();

        var promotion = CreateWith(sourceReference: $"  recruitment:application:{applicationId}  ");

        Assert.Equal($"recruitment:application:{applicationId}", promotion.SourceReference);
    }

    [Theory]
    [InlineData("recruitment:application:0b8f6f39-8e3b-4ac3-9d53-0b3c7f1d2a11", true)]
    [InlineData("recruitment:application:", true)]
    [InlineData("recruitment:vacancy:0b8f6f39", false)]
    [InlineData("Recruitment:Application:0b8f6f39", false)]
    [InlineData("import:batch:42", false)]
    public void IsInternalAppointment_Requires_Exact_Recruitment_Application_Prefix(string sourceReference, bool expected)
    {
        Assert.Equal(expected, CreateWith(sourceReference: sourceReference).IsInternalAppointment);
    }

    [Fact]
    public void ResolveManagerId_Keeps_Current_Manager_When_None_Specified()
    {
        var current = Guid.NewGuid();

        Assert.Equal(current, CreateWith().ResolveManagerId(current));
    }

    [Fact]
    public void ResolveManagerId_Uses_New_Manager_When_Specified()
    {
        var newManager = Guid.NewGuid();

        Assert.Equal(newManager, CreateWith(newManagerId: newManager).ResolveManagerId(Guid.NewGuid()));
    }

    [Fact]
    public void ResolveManagerId_Returns_Null_When_Clearing_Manager()
    {
        Assert.Null(CreateWith(clearsManager: true).ResolveManagerId(Guid.NewGuid()));
    }

    [Fact]
    public void ResolveManagerId_Returns_Null_When_No_Current_And_None_Specified()
    {
        Assert.Null(CreateWith().ResolveManagerId(null));
    }
}
