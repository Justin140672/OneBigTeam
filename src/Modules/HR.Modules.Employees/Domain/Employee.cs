using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Employees.Domain;

internal sealed class Employee
{
    private Employee() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public Guid? LocationId { get; private set; }
    public Guid? PositionProfileId { get; private set; }
    public Guid? ManagerId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string WorkEmail { get; private set; } = string.Empty;
    public string? PersonalEmail { get; private set; }
    public DateOnly StartDate { get; private set; }
    public EmploymentStatus Status { get; private set; }
    public bool HasSystemAccess { get; private set; }
    public WorkingDays? WorkingDaysOverride { get; private set; }
    public decimal? HoursPerDayOverride { get; private set; }
    public string? ProfileImageUrl { get; private set; }
    public string? PreferredName { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    public string? Nationality { get; private set; }
    public string? Gender { get; private set; }
    public string? GenderOther { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? HomePhone { get; private set; }
    public string? AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string? City { get; private set; }
    public string? County { get; private set; }
    public string? PostCode { get; private set; }
    public string? Country { get; private set; }
    public string? EmployeeNumber { get; private set; }
    public Guid? EmploymentTypeId { get; private set; }
    public DateOnly? ContinuousServiceDate { get; private set; }
    public DateOnly? ProbationEndDate { get; private set; }
    public DateOnly? LeavingDate { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Employee Create(
        Guid id,
        Guid companyId,
        string firstName,
        string lastName,
        string workEmail,
        DateOnly startDate,
        bool hasSystemAccess,
        DateTimeOffset now)
    {
        return new Employee
        {
            Id = id,
            CompanyId = companyId,
            FirstName = firstName,
            LastName = lastName,
            WorkEmail = workEmail,
            StartDate = startDate,
            Status = EmploymentStatus.Draft,
            HasSystemAccess = hasSystemAccess,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Assign(Guid? departmentId, Guid? positionProfileId, Guid? locationId, Guid? managerId, DateTimeOffset now)
    {
        DepartmentId = departmentId;
        PositionProfileId = positionProfileId;
        LocationId = locationId;
        ManagerId = managerId;
        UpdatedAt = now;
    }

    public void SetSystemAccess(bool hasSystemAccess, DateTimeOffset now)
    {
        HasSystemAccess = hasSystemAccess;
        UpdatedAt = now;
    }

    public void UpdateProfile(
        string firstName,
        string lastName,
        string workEmail,
        string? personalEmail,
        DateOnly startDate,
        DateTimeOffset now)
    {
        FirstName = firstName;
        LastName = lastName;
        WorkEmail = workEmail;
        PersonalEmail = personalEmail;
        StartDate = startDate;
        UpdatedAt = now;
    }

    public void SetWorkingPattern(WorkingDays? workingDays, decimal? hoursPerDay, DateTimeOffset now)
    {
        WorkingDaysOverride = workingDays;
        HoursPerDayOverride = hoursPerDay;
        UpdatedAt = now;
    }

    public void SetProbationEndDate(DateOnly probationEndDate, DateTimeOffset now)
    {
        ProbationEndDate = probationEndDate;
        UpdatedAt = now;
    }

    public void SetProfileImage(string? url, DateTimeOffset now)
    {
        ProfileImageUrl = url;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        Status = EmploymentStatus.Active;
        UpdatedAt = now;
    }

    public void SetOnLeave(DateTimeOffset now)
    {
        Status = EmploymentStatus.OnLeave;
        UpdatedAt = now;
    }

    public void Suspend(DateTimeOffset now)
    {
        Status = EmploymentStatus.Suspended;
        UpdatedAt = now;
    }

    public void Terminate(DateTimeOffset now)
    {
        Status = EmploymentStatus.Terminated;
        UpdatedAt = now;
    }

    public void UpdatePersonalDetails(
        string? preferredName,
        DateOnly? dateOfBirth,
        string? nationality,
        string? gender,
        string? genderOther,
        DateTimeOffset now)
    {
        PreferredName = Norm(preferredName);
        DateOfBirth   = dateOfBirth;
        Nationality   = Norm(nationality);
        Gender        = Norm(gender);
        GenderOther   = Norm(genderOther);
        UpdatedAt     = now;
    }

    public void UpdateContactDetails(
        string? personalEmail,
        string? phoneNumber,
        string? homePhone,
        string? addressLine1,
        string? addressLine2,
        string? city,
        string? county,
        string? postCode,
        string? country,
        DateTimeOffset now)
    {
        PersonalEmail = Norm(personalEmail);
        PhoneNumber   = Norm(phoneNumber);
        HomePhone     = Norm(homePhone);
        AddressLine1  = Norm(addressLine1);
        AddressLine2  = Norm(addressLine2);
        City          = Norm(city);
        County        = Norm(county);
        PostCode      = Norm(postCode);
        Country       = Norm(country);
        UpdatedAt     = now;
    }

    public void UpdateEmploymentDetails(
        string? employeeNumber,
        Guid? employmentTypeId,
        DateOnly startDate,
        DateOnly? continuousServiceDate,
        DateOnly? probationEndDate,
        DateOnly? leavingDate,
        string? notes,
        DateTimeOffset now)
    {
        EmployeeNumber        = Norm(employeeNumber);
        EmploymentTypeId      = employmentTypeId;
        StartDate             = startDate;
        ContinuousServiceDate = continuousServiceDate;
        ProbationEndDate      = probationEndDate;
        LeavingDate           = leavingDate;
        Notes                 = Norm(notes);
        UpdatedAt             = now;
    }

    private static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
