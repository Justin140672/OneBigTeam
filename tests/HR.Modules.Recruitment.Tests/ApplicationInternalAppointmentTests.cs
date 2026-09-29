using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 7: the Application's internal-appointment state machine
/// (null → Pending → Completed, with Pending → null on abandon).
/// </summary>
public class ApplicationInternalAppointmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private static Application Create(ApplicationSource? source = ApplicationSource.Internal, Guid? stageId = null) =>
        Application.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), stageId ?? Guid.NewGuid(), null, Now.AddDays(-5), source);

    [Fact]
    public void New_Application_Has_No_Appointment()
    {
        var application = Create();

        Assert.Null(application.AppointmentStatus);
        Assert.False(application.HasInternalAppointmentInProgress);
    }

    [Fact]
    public void InternalAppointmentSourceReference_Is_Stable_And_Keyed_By_Application_Id()
    {
        var application = Create();

        Assert.Equal($"recruitment:application:{application.Id}", application.InternalAppointmentSourceReference);
        Assert.Equal(application.InternalAppointmentSourceReference, application.InternalAppointmentSourceReference);
    }


    [Fact]
    public void Begin_Sets_Pending_And_Requester()
    {
        var application = Create();
        var employeeId = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();
        var stageBefore = application.CurrentStageId;

        application.BeginInternalAppointment(employeeId, requestedBy, Now);

        Assert.Equal(InternalAppointmentStatus.Pending, application.AppointmentStatus);
        Assert.True(application.HasInternalAppointmentInProgress);
        Assert.Equal(employeeId, application.AppointmentEmployeeId);
        Assert.Equal(requestedBy, application.AppointmentRequestedByUserId);
        Assert.Equal(Now, application.AppointmentRequestedAt);
        Assert.Equal(Now, application.UpdatedAt);
        Assert.Equal(stageBefore, application.CurrentStageId);
        Assert.Null(application.AppointmentPromotionId);
        Assert.Null(application.AppointmentCompletedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Direct")]
    [InlineData("Referral")]
    [InlineData("ExternalRecruiter")]
    [InlineData("JobBoard")]
    [InlineData("CareersSite")]
    [InlineData("Unspecified")]
    public void Begin_Throws_For_Non_Internal_Application(string? source)
    {
        var application = Create(source is null ? null : Enum.Parse<ApplicationSource>(source));

        Assert.Throws<InvalidOperationException>(() =>
            application.BeginInternalAppointment(Guid.NewGuid(), Guid.NewGuid(), Now));
        Assert.Null(application.AppointmentStatus);
    }

    [Fact]
    public void Begin_Is_Reentrant_While_Pending_And_Refreshes_Requester()
    {
        var application = Create();
        application.BeginInternalAppointment(Guid.NewGuid(), Guid.NewGuid(), Now);
        var retriedBy = Guid.NewGuid();

        application.BeginInternalAppointment(application.AppointmentEmployeeId!.Value, retriedBy, Now.AddMinutes(5));

        Assert.Equal(InternalAppointmentStatus.Pending, application.AppointmentStatus);
        Assert.Equal(retriedBy, application.AppointmentRequestedByUserId);
        Assert.Equal(Now.AddMinutes(5), application.AppointmentRequestedAt);
    }

    [Fact]
    public void Begin_Throws_When_Already_Completed()
    {
        var application = Create();
        application.BeginInternalAppointment(Guid.NewGuid(), Guid.NewGuid(), Now);
        application.CompleteInternalAppointment(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), Now);

        Assert.Throws<InvalidOperationException>(() =>
            application.BeginInternalAppointment(Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(1)));
        Assert.Equal(InternalAppointmentStatus.Completed, application.AppointmentStatus);
    }


    [Fact]
    public void Abandon_Clears_Pending_Appointment()
    {
        var stageId = Guid.NewGuid();
        var application = Create(stageId: stageId);
        application.BeginInternalAppointment(Guid.NewGuid(), Guid.NewGuid(), Now);

        var released = application.AbandonInternalAppointment(Now.AddMinutes(1));

        Assert.True(released);
        Assert.Null(application.AppointmentStatus);
        Assert.Null(application.AppointmentEmployeeId);
        Assert.Null(application.AppointmentRequestedByUserId);
        Assert.Null(application.AppointmentRequestedAt);
        Assert.False(application.HasInternalAppointmentInProgress);
        Assert.Equal(stageId, application.CurrentStageId);
        Assert.Equal(Now.AddMinutes(1), application.UpdatedAt);
    }

    [Fact]
    public void Abandon_Is_NoOp_When_No_Appointment_Started()
    {
        var application = Create();
        var updatedAt = application.UpdatedAt;

        var released = application.AbandonInternalAppointment(Now);

        Assert.False(released);
        Assert.Null(application.AppointmentStatus);
        Assert.Equal(updatedAt, application.UpdatedAt);
    }

    [Fact]
    public void Abandon_Is_NoOp_When_Completed()
    {
        var application = Create();
        var employeeId = Guid.NewGuid();
        var promotionId = Guid.NewGuid();
        application.BeginInternalAppointment(employeeId, Guid.NewGuid(), Now);
        application.CompleteInternalAppointment(Guid.NewGuid(), promotionId, new DateOnly(2026, 10, 1), Now);

        var released = application.AbandonInternalAppointment(Now.AddMinutes(1));

        Assert.False(released);
        Assert.Equal(InternalAppointmentStatus.Completed, application.AppointmentStatus);
        Assert.Equal(employeeId, application.AppointmentEmployeeId);
        Assert.Equal(promotionId, application.AppointmentPromotionId);
        Assert.Equal(Now, application.UpdatedAt);
    }

    [Fact]
    public void Abandon_Twice_Only_Releases_Once()
    {
        var application = Create();
        application.BeginInternalAppointment(Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.True(application.AbandonInternalAppointment(Now));
        Assert.False(application.AbandonInternalAppointment(Now));
    }


    [Fact]
    public void Complete_Moves_To_Hired_And_Records_Promotion()
    {
        var application = Create();
        var employeeId = Guid.NewGuid();
        application.BeginInternalAppointment(employeeId, Guid.NewGuid(), Now);
        var hiredStageId = Guid.NewGuid();
        var promotionId = Guid.NewGuid();
        var effectiveDate = new DateOnly(2026, 10, 1);
        var completedAt = Now.AddSeconds(3);

        application.CompleteInternalAppointment(hiredStageId, promotionId, effectiveDate, completedAt);

        Assert.Equal(hiredStageId, application.CurrentStageId);
        Assert.Equal(InternalAppointmentStatus.Completed, application.AppointmentStatus);
        Assert.Equal(promotionId, application.AppointmentPromotionId);
        Assert.Equal(effectiveDate, application.AppointmentEffectiveDate);
        Assert.Equal(completedAt, application.AppointmentCompletedAt);
        Assert.Equal(completedAt, application.UpdatedAt);
        Assert.Equal(employeeId, application.AppointmentEmployeeId);
        Assert.False(application.HasInternalAppointmentInProgress);
    }

    [Fact]
    public void Complete_Throws_When_Not_Started()
    {
        var stageId = Guid.NewGuid();
        var application = Create(stageId: stageId);

        Assert.Throws<InvalidOperationException>(() =>
            application.CompleteInternalAppointment(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), Now));
        Assert.Equal(stageId, application.CurrentStageId);
        Assert.Null(application.AppointmentStatus);
    }

    [Fact]
    public void Complete_Throws_When_Already_Completed()
    {
        var application = Create();
        application.BeginInternalAppointment(Guid.NewGuid(), Guid.NewGuid(), Now);
        var hiredStageId = Guid.NewGuid();
        var promotionId = Guid.NewGuid();
        application.CompleteInternalAppointment(hiredStageId, promotionId, new DateOnly(2026, 10, 1), Now);

        Assert.Throws<InvalidOperationException>(() =>
            application.CompleteInternalAppointment(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 11, 1), Now.AddDays(1)));

        Assert.Equal(hiredStageId, application.CurrentStageId);
        Assert.Equal(promotionId, application.AppointmentPromotionId);
        Assert.Equal(new DateOnly(2026, 10, 1), application.AppointmentEffectiveDate);
    }

    [Fact]
    public void Complete_Throws_After_Abandon()
    {
        var application = Create();
        application.BeginInternalAppointment(Guid.NewGuid(), Guid.NewGuid(), Now);
        application.AbandonInternalAppointment(Now);

        Assert.Throws<InvalidOperationException>(() =>
            application.CompleteInternalAppointment(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), Now));
    }
}
