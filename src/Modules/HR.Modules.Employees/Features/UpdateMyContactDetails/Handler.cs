using System.Text.RegularExpressions;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.UpdateMyContactDetails;

internal sealed class UpdateMyContactDetailsHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IAuditEventPublisher _auditEventPublisher;
    private readonly ICompanyContactValidationReader _contactValidationReader;

    public UpdateMyContactDetailsHandler(
        EmployeesDbContext dbContext,
        IClock clock,
        IAuditEventPublisher auditEventPublisher,
        ICompanyContactValidationReader contactValidationReader)
    {
        _dbContext = dbContext;
        _clock = clock;
        _auditEventPublisher = auditEventPublisher;
        _contactValidationReader = contactValidationReader;
    }

    public async Task<Result<UpdateMyContactDetailsResponse>> HandleAsync(
        UpdateMyContactDetailsRequest request,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var contactRules = await _contactValidationReader.GetContactValidationRulesAsync(request.CompanyId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.PostCode) &&
            !Regex.IsMatch(request.PostCode.Trim(), contactRules.PostcodeRegex, RegexOptions.IgnoreCase))
            return Result.Failure<UpdateMyContactDetailsResponse>(Error.Validation($"'{request.PostCode.Trim()}' is not a valid postcode."));

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) &&
            !Regex.IsMatch(request.PhoneNumber.Trim(), contactRules.MobileRegex, RegexOptions.IgnoreCase))
            return Result.Failure<UpdateMyContactDetailsResponse>(Error.Validation($"'{request.PhoneNumber.Trim()}' is not a valid mobile number."));

        if (!string.IsNullOrWhiteSpace(request.HomePhone) &&
            !Regex.IsMatch(request.HomePhone.Trim(), contactRules.TelephoneRegex, RegexOptions.IgnoreCase))
            return Result.Failure<UpdateMyContactDetailsResponse>(Error.Validation($"'{request.HomePhone.Trim()}' is not a valid phone number."));

        var employee = await _dbContext.Employees
            .SingleOrDefaultAsync(
                e => e.CompanyId == request.CompanyId && e.Id == employeeId,
                cancellationToken);

        if (employee is null)
            return Result.Failure<UpdateMyContactDetailsResponse>(
                Error.NotFound("No employee record is linked to this user."));

        var now = _clock.UtcNowOffset();

        var before = new ContactDetailsSnapshot(
            employee.PersonalEmail,
            employee.PhoneNumber,
            employee.HomePhone,
            employee.AddressLine1,
            employee.AddressLine2,
            employee.City,
            employee.County,
            employee.PostCode,
            employee.Country);

        employee.UpdateContactDetails(
            request.PersonalEmail,
            request.PhoneNumber,
            request.HomePhone,
            request.AddressLine1,
            request.AddressLine2,
            request.City,
            request.County,
            request.PostCode,
            request.Country,
            now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var after = new ContactDetailsSnapshot(
            employee.PersonalEmail,
            employee.PhoneNumber,
            employee.HomePhone,
            employee.AddressLine1,
            employee.AddressLine2,
            employee.City,
            employee.County,
            employee.PostCode,
            employee.Country);

        await _auditEventPublisher.PublishAsync(
            new ContactDetailsUpdatedAuditEvent(employee.CompanyId, employee.Id, employeeId, now, before, after),
            cancellationToken);

        return Result.Success(new UpdateMyContactDetailsResponse(
            employee.WorkEmail,
            employee.PersonalEmail,
            employee.PhoneNumber,
            employee.HomePhone,
            employee.AddressLine1,
            employee.AddressLine2,
            employee.City,
            employee.County,
            employee.PostCode,
            employee.Country));
    }
}
