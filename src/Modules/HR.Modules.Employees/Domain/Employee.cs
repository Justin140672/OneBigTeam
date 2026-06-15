using HR.SharedKernel;

namespace HR.Modules.Employees.Domain;

internal sealed class Employee
{
    private Employee() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? DepartmentId { get; private set; }
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

    public void Assign(Guid? departmentId, Guid? positionProfileId, Guid? managerId, DateTimeOffset now)
    {
        DepartmentId = departmentId;
        PositionProfileId = positionProfileId;
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

}
