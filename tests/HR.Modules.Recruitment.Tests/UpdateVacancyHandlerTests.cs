using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UpdateVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class UpdateVacancyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Updates_Vacancy_Details()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Old Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var newHiringManagerId = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId         = companyId,
                VacancyId         = vacancy.Id,
                AdvertTitle       = "New Title",
                AdvertDescription = "Updated description",
                HiringManagerId   = newHiringManagerId,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", result.Value!.AdvertTitle);
        Assert.Equal("Updated description", result.Value.AdvertDescription);
        Assert.Equal(newHiringManagerId, result.Value.HiringManagerId);

        var published = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<VacancyUpdatedAuditEvent>(published);
        Assert.Equal("vacancy.updated", ((IAuditEvent)auditEvent).EventType);
        Assert.Equal("Vacancy", ((IAuditEvent)auditEvent).EntityType);
        Assert.Equal(vacancy.Id, ((IAuditEvent)auditEvent).EntityId);
        Assert.Equal("Old Title", auditEvent.Before.AdvertTitle);
        Assert.Equal("New Title", auditEvent.After.AdvertTitle);
        Assert.Equal("New Title", auditEvent.EffectiveTitle);
    }

    [Fact]
    public async Task HandleAsync_Propagates_AssignedRecruiterId_To_Response()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Old Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);

        // Ticket #81: AssignedRecruiterId now references ExternalRecruiter (in this same module/schema)
        // rather than an unvalidated Employee id, so the handler validates existence/company-ownership —
        // a real, active ExternalRecruiter row must exist for this to succeed.
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await handler(db, new FakeAuditPublisher()).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId           = companyId,
                VacancyId           = vacancy.Id,
                AdvertTitle         = "New Title",
                HiringManagerId     = Guid.NewGuid(),
                AssignedRecruiterId = recruiter.Id,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(recruiter.Id, result.Value!.AssignedRecruiterId);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal(recruiter.Id, saved.AssignedRecruiterId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_AssignedRecruiter_Is_Inactive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Old Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);

        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        recruiter.SetActiveStatus(false, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId           = companyId,
                VacancyId           = vacancy.Id,
                AdvertTitle         = "New Title",
                HiringManagerId     = vacancy.HiringManagerId,
                AssignedRecruiterId = recruiter.Id,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("inactive", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(auditPublisher.Published);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Null(saved.AssignedRecruiterId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_AssignedRecruiter_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Old Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);

        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), otherCompanyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId           = companyId,
                VacancyId           = vacancy.Id,
                AdvertTitle         = "New Title",
                HiringManagerId     = vacancy.HiringManagerId,
                AssignedRecruiterId = recruiter.Id,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.Published);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Null(saved.AssignedRecruiterId);
    }

    [Fact]
    public async Task HandleAsync_Clears_AssignedRecruiterId_To_Null_Succeeds_Since_It_Is_Optional()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Old Title", null, Guid.NewGuid(), Now, recruiter.Id);
        db.ExternalRecruiters.Add(recruiter);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId           = companyId,
                VacancyId           = vacancy.Id,
                AdvertTitle         = "Old Title",
                HiringManagerId     = vacancy.HiringManagerId,
                AssignedRecruiterId = null,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.AssignedRecruiterId);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Null(saved.AssignedRecruiterId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId       = Guid.NewGuid(),
                VacancyId       = Guid.NewGuid(),
                AdvertTitle     = "Title",
                HiringManagerId = Guid.NewGuid(),
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Clears_AdvertTitle_Back_To_Null_Succeeds()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Old Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId       = companyId,
                VacancyId       = vacancy.Id,
                AdvertTitle     = null,
                HiringManagerId = vacancy.HiringManagerId,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.AdvertTitle);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Null(saved.AdvertTitle);
    }

    [Fact]
    public async Task HandleAsync_EffectiveTitle_Equals_AdvertTitle_When_Set()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Old Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Position Profile Title", null, null, true, null, null),
        };
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId       = companyId,
                VacancyId       = vacancy.Id,
                AdvertTitle     = "New Advert Title",
                HiringManagerId = vacancy.HiringManagerId,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.IsType<VacancyUpdatedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal("New Advert Title", auditEvent.EffectiveTitle);
    }

    [Fact]
    public async Task HandleAsync_EffectiveTitle_Resolves_To_PositionProfile_Title_When_AdvertTitle_Is_Null()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Old Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Position Profile Title", null, null, true, null, null),
        };
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId       = companyId,
                VacancyId       = vacancy.Id,
                AdvertTitle     = null,
                HiringManagerId = vacancy.HiringManagerId,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.IsType<VacancyUpdatedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal("Position Profile Title", auditEvent.EffectiveTitle);
    }

    [Fact]
    public async Task HandleAsync_EffectiveTitle_Falls_Back_To_Untitled_When_No_PositionProfile_And_AdvertTitle_Null()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Old Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        // No summaries dictionary supplied — simulates the linked profile no longer being resolvable.
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId       = companyId,
                VacancyId       = vacancy.Id,
                AdvertTitle     = null,
                HiringManagerId = vacancy.HiringManagerId,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.IsType<VacancyUpdatedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal("(untitled)", auditEvent.EffectiveTitle);
    }

    [Theory]
    [InlineData((int)VacancyStatus.Draft, 0, true)]
    [InlineData((int)VacancyStatus.Draft, 1, false)]
    [InlineData((int)VacancyStatus.Draft, 5, false)]
    [InlineData((int)VacancyStatus.Open, 0, false)]
    [InlineData((int)VacancyStatus.OnHold, 0, false)]
    [InlineData((int)VacancyStatus.Closed, 0, false)]
    [InlineData((int)VacancyStatus.Cancelled, 0, false)]
    public void CanChangePositionProfile_Reflects_Status_And_ApplicationCount(
        int statusValue, int applicationCount, bool expected)
    {
        var status = (VacancyStatus)statusValue;
        Assert.Equal(expected, UpdateVacancyHandler.CanChangePositionProfile(status, applicationCount));
    }

    [Fact]
    public async Task HandleAsync_Changes_PositionProfileId_When_Draft_With_Zero_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldPositionProfileId = Guid.NewGuid();
        var newPositionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, oldPositionProfileId, "Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var reader = new FakePositionProfileReader(matchingCompanyId: companyId, matchingPositionProfileId: newPositionProfileId);

        var result = await handler(db, auditPublisher, reader).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId         = companyId,
                VacancyId         = vacancy.Id,
                PositionProfileId = newPositionProfileId,
                AdvertTitle       = "Title",
                HiringManagerId   = vacancy.HiringManagerId,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newPositionProfileId, result.Value!.PositionProfileId);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal(newPositionProfileId, saved.PositionProfileId);

        var assignedEvent = Assert.Single(auditPublisher.Published.OfType<VacancyPositionProfileAssignedAuditEvent>());
        Assert.Equal("update", assignedEvent.AssignmentMethod);
        Assert.Equal(oldPositionProfileId, assignedEvent.PreviousPositionProfileId);
        Assert.Equal(newPositionProfileId, assignedEvent.PositionProfileId);
        Assert.Equal(vacancy.Id, assignedEvent.VacancyId);

        // The standard VacancyUpdatedAuditEvent still fires alongside the position-profile-change event.
        Assert.Single(auditPublisher.Published.OfType<VacancyUpdatedAuditEvent>());
    }

    [Fact]
    public async Task HandleAsync_Rejects_PositionProfileId_Change_When_Vacancy_Is_Not_Draft()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldPositionProfileId = Guid.NewGuid();
        var newPositionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, oldPositionProfileId, "Title", null, Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId         = companyId,
                VacancyId         = vacancy.Id,
                PositionProfileId = newPositionProfileId,
                AdvertTitle       = "Title",
                HiringManagerId   = vacancy.HiringManagerId,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("Position Profile cannot be changed", result.Error.Message);
        Assert.Empty(auditPublisher.Published);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal(oldPositionProfileId, saved.PositionProfileId);
    }

    [Fact]
    public async Task HandleAsync_Rejects_PositionProfileId_Change_When_Vacancy_Has_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldPositionProfileId = Guid.NewGuid();
        var newPositionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, oldPositionProfileId, "Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        db.Applications.Add(Application.Create(Guid.NewGuid(), companyId, vacancy.Id, Guid.NewGuid(), null, Now));
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId         = companyId,
                VacancyId         = vacancy.Id,
                PositionProfileId = newPositionProfileId,
                AdvertTitle       = "Title",
                HiringManagerId   = vacancy.HiringManagerId,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(auditPublisher.Published);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal(oldPositionProfileId, saved.PositionProfileId);
    }

    [Fact]
    public async Task HandleAsync_Rejects_PositionProfileId_Change_When_Target_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldPositionProfileId = Guid.NewGuid();
        var newPositionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, oldPositionProfileId, "Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        // The reader is configured to match a different company than the request, simulating a
        // position profile that belongs to another company (cross-company rejection).
        var reader = new FakePositionProfileReader(matchingCompanyId: Guid.NewGuid(), matchingPositionProfileId: newPositionProfileId);

        var result = await handler(db, auditPublisher, reader).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId         = companyId,
                VacancyId         = vacancy.Id,
                PositionProfileId = newPositionProfileId,
                AdvertTitle       = "Title",
                HiringManagerId   = vacancy.HiringManagerId,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.Published);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal(oldPositionProfileId, saved.PositionProfileId);
    }

    [Fact]
    public async Task HandleAsync_Same_PositionProfileId_As_Current_Is_A_NoOp_For_PositionProfile_Change()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Old Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId         = companyId,
                VacancyId         = vacancy.Id,
                PositionProfileId = positionProfileId,
                AdvertTitle       = "New Title",
                HiringManagerId   = vacancy.HiringManagerId,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(positionProfileId, result.Value!.PositionProfileId);
        Assert.Equal("New Title", result.Value.AdvertTitle);
        Assert.Empty(auditPublisher.Published.OfType<VacancyPositionProfileAssignedAuditEvent>());
        Assert.Single(auditPublisher.Published.OfType<VacancyUpdatedAuditEvent>());
    }

    [Fact]
    public async Task HandleAsync_Null_PositionProfileId_Leaves_Vacancy_PositionProfileId_Untouched()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Old Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId         = companyId,
                VacancyId         = vacancy.Id,
                PositionProfileId = null,
                AdvertTitle       = "New Title",
                HiringManagerId   = vacancy.HiringManagerId,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(positionProfileId, result.Value!.PositionProfileId);
        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal(positionProfileId, saved.PositionProfileId);
        Assert.Empty(auditPublisher.Published.OfType<VacancyPositionProfileAssignedAuditEvent>());
    }

    [Fact]
    public async Task HandleAsync_Allows_PositionProfileId_Change_When_Not_Draft_With_AuthorisedCorrection()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldPositionProfileId = Guid.NewGuid();
        var newPositionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, oldPositionProfileId, "Title", null, Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var reader = new FakePositionProfileReader(matchingCompanyId: companyId, matchingPositionProfileId: newPositionProfileId);
        var performedBy = Guid.NewGuid();

        var result = await handler(db, auditPublisher, reader).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId             = companyId,
                VacancyId             = vacancy.Id,
                PositionProfileId     = newPositionProfileId,
                AdvertTitle           = "Title",
                HiringManagerId       = vacancy.HiringManagerId,
                IsAuthorisedCorrection = true,
                CorrectionReason      = "Vacancy created against the wrong position profile.",
            },
            performedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newPositionProfileId, result.Value!.PositionProfileId);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal(newPositionProfileId, saved.PositionProfileId);

        var assignedEvent = Assert.Single(auditPublisher.Published.OfType<VacancyPositionProfileAssignedAuditEvent>());
        Assert.Equal("authorised_correction", assignedEvent.AssignmentMethod);
        Assert.Equal(oldPositionProfileId, assignedEvent.PreviousPositionProfileId);
        Assert.Equal(newPositionProfileId, assignedEvent.PositionProfileId);
        Assert.Equal(performedBy, ((IAuditEvent)assignedEvent).ActorUserId);
        Assert.Equal(performedBy, assignedEvent.PerformedBy);
        Assert.Equal("Vacancy created against the wrong position profile.", assignedEvent.CorrectionReason);
    }

    [Fact]
    public async Task HandleAsync_Allows_PositionProfileId_Change_When_Has_Applications_With_AuthorisedCorrection()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldPositionProfileId = Guid.NewGuid();
        var newPositionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, oldPositionProfileId, "Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        db.Applications.Add(Application.Create(Guid.NewGuid(), companyId, vacancy.Id, Guid.NewGuid(), null, Now));
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var reader = new FakePositionProfileReader(matchingCompanyId: companyId, matchingPositionProfileId: newPositionProfileId);
        var performedBy = Guid.NewGuid();

        var result = await handler(db, auditPublisher, reader).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId              = companyId,
                VacancyId              = vacancy.Id,
                PositionProfileId      = newPositionProfileId,
                AdvertTitle            = "Title",
                HiringManagerId        = vacancy.HiringManagerId,
                IsAuthorisedCorrection = true,
                CorrectionReason       = "Position profile was mismatched at creation time.",
            },
            performedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal(newPositionProfileId, saved.PositionProfileId);

        var assignedEvent = Assert.Single(auditPublisher.Published.OfType<VacancyPositionProfileAssignedAuditEvent>());
        Assert.Equal("authorised_correction", assignedEvent.AssignmentMethod);
        Assert.Equal(performedBy, assignedEvent.PerformedBy);
    }

    [Fact]
    public async Task HandleAsync_Rejects_PositionProfileId_Change_When_AuthorisedCorrection_Flag_Set_But_Reason_Missing()
    {
        // Defense in depth: even though the validator also requires a reason whenever
        // IsAuthorisedCorrection is true, the handler independently guards against a
        // null/whitespace CorrectionReason so it can never bypass the change-control check.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldPositionProfileId = Guid.NewGuid();
        var newPositionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, oldPositionProfileId, "Title", null, Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId              = companyId,
                VacancyId              = vacancy.Id,
                PositionProfileId      = newPositionProfileId,
                AdvertTitle            = "Title",
                HiringManagerId        = vacancy.HiringManagerId,
                IsAuthorisedCorrection = true,
                CorrectionReason       = "   ",
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(auditPublisher.Published);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal(oldPositionProfileId, saved.PositionProfileId);
    }

    [Fact]
    public async Task ChangePositionProfile_Does_Not_Touch_AdvertTitle_Or_AdvertDescription()
    {
        // Vacancy.ChangePositionProfile only ever sets PositionProfileId and UpdatedAt — proves the
        // domain method itself carries no side effect on advert fields, independent of whatever the
        // handler separately does via UpdateDetails.
        var vacancy = Vacancy.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Original Advert Title", "Original Advert Description", Guid.NewGuid(), Now);

        vacancy.ChangePositionProfile(Guid.NewGuid(), Now.AddMinutes(5));

        Assert.Equal("Original Advert Title", vacancy.AdvertTitle);
        Assert.Equal("Original Advert Description", vacancy.AdvertDescription);
    }

    [Fact]
    public async Task HandleAsync_AuthorisedCorrection_PositionProfile_Change_Applies_Requested_AdvertTitle_And_Description_Via_UpdateDetails_Not_As_A_SideEffect_Of_The_ProfileChange()
    {
        // The handler always calls UpdateDetails separately from ChangePositionProfile, so
        // AdvertTitle/AdvertDescription end up reflecting whatever the request explicitly supplied —
        // not silently cleared or left stale as a side effect of the Position Profile change itself.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldPositionProfileId = Guid.NewGuid();
        var newPositionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(
            Guid.NewGuid(), companyId, oldPositionProfileId,
            "Original Advert Title", "Original Advert Description", Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var reader = new FakePositionProfileReader(matchingCompanyId: companyId, matchingPositionProfileId: newPositionProfileId);

        var result = await handler(db, auditPublisher, reader).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId              = companyId,
                VacancyId              = vacancy.Id,
                PositionProfileId      = newPositionProfileId,
                AdvertTitle            = "Original Advert Title",
                AdvertDescription      = "Original Advert Description",
                HiringManagerId        = vacancy.HiringManagerId,
                IsAuthorisedCorrection = true,
                CorrectionReason       = "Correcting an earlier data-entry mistake.",
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Original Advert Title", result.Value!.AdvertTitle);
        Assert.Equal("Original Advert Description", result.Value.AdvertDescription);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal("Original Advert Title", saved.AdvertTitle);
        Assert.Equal("Original Advert Description", saved.AdvertDescription);
        Assert.Equal(newPositionProfileId, saved.PositionProfileId);
    }

    private static UpdateVacancyHandler handler(
        RecruitmentDbContext db,
        FakeAuditPublisher? auditPublisher = null,
        IPositionProfileReader? positionProfileReader = null) =>
        new(db, new FakeClock(FixedUtcNow), auditPublisher ?? new FakeAuditPublisher(), positionProfileReader ?? new FakePositionProfileReader());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
