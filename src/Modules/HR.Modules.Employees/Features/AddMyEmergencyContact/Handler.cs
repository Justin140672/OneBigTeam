using System.Text.RegularExpressions;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.AddMyEmergencyContact;

internal sealed class AddMyEmergencyContactHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ICompanyContactValidationReader contactValidationReader)
{
    public async Task<Result<AddMyEmergencyContactResponse>> HandleAsync(
        AddMyEmergencyContactRequest request,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, AddMyEmergencyContactResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<AddMyEmergencyContactResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var employeeExists = await dbContext.Employees
            .AnyAsync(e => e.CompanyId == request.CompanyId && e.Id == employeeId, cancellationToken);

        if (!employeeExists)
            return Result.Failure<AddMyEmergencyContactResponse>(
                Error.NotFound("No employee record is linked to this user."));

        var contactRules = await contactValidationReader.GetContactValidationRulesAsync(request.CompanyId, cancellationToken);
        var phone = request.PhoneNumber.Trim();

        if (!Regex.IsMatch(phone, contactRules.MobileRegex, RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(phone, contactRules.TelephoneRegex, RegexOptions.IgnoreCase))
            return Result.Failure<AddMyEmergencyContactResponse>(Error.Validation($"'{phone}' is not a valid phone number."));

        var now = clock.UtcNowOffset();

        var contact = EmergencyContact.Create(
            Guid.NewGuid(),
            employeeId,
            request.CompanyId,
            request.Name,
            request.Relationship,
            request.PhoneNumber,
            request.Email,
            now);

        dbContext.EmergencyContacts.Add(contact);

        var response = new AddMyEmergencyContactResponse(
            contact.Id, contact.Name, contact.Relationship, contact.PhoneNumber, contact.Email);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync<IdempotencyRecord, AddMyEmergencyContactResponse>(dbContext.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new EmergencyContactAddedAuditEvent(
                request.CompanyId, employeeId, employeeId, now, contact.Id, contact.Name, contact.Relationship),
            cancellationToken);

        return Result.Success(response);
    }
}
