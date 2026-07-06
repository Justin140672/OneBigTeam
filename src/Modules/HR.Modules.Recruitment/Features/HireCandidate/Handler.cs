using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.HireCandidate;

internal sealed class HireCandidateHandler(
    RecruitmentDbContext db,
    IEmployeeProvisioningService employeeProvisioningService,
    IClock clock)
{
    public async Task<Result<HireCandidateResponse>> HandleAsync(
        HireCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Result.Failure<HireCandidateResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.Status != ApplicationStatus.Offered)
            return Result.Failure<HireCandidateResponse>(
                Error.Validation($"Cannot hire an application with status '{application.Status}'."));

        var candidate = await db.Candidates
            .SingleOrDefaultAsync(c => c.Id == application.CandidateId && c.CompanyId == request.CompanyId, cancellationToken);

        if (candidate is null)
            return Result.Failure<HireCandidateResponse>(
                Error.NotFound($"Candidate '{application.CandidateId}' was not found."));

        if (candidate.EmployeeId is not null)
            return Result.Failure<HireCandidateResponse>(
                Error.Conflict("This candidate is already linked to an employee."));

        var provisioningResult = await employeeProvisioningService.CreateFromCandidateAsync(
            new EmployeeProvisioningRequest(
                request.CompanyId,
                candidate.FirstName,
                candidate.LastName,
                candidate.Email,
                request.StartDate,
                request.DateOfBirth,
                request.Nationality,
                request.Gender,
                request.GenderOther,
                PersonalEmail: null,
                candidate.Phone,
                request.DepartmentId,
                request.PositionProfileId,
                request.ManagerId),
            cancellationToken);

        if (provisioningResult.IsFailure)
            return Result.Failure<HireCandidateResponse>(provisioningResult.Error);

        var now = clock.UtcNowOffset();

        application.Hire(now);
        candidate.LinkToEmployee(provisioningResult.Value!, now);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new HireCandidateResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            provisioningResult.Value!,
            application.Status,
            application.InterviewOutcome,
            application.Notes,
            application.AppliedAt,
            application.CreatedAt,
            application.UpdatedAt));
    }
}
