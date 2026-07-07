using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class AppSession(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public bool IsLoaded { get; private set; }

    // Identity
    public Guid UserId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string? Email { get; private set; }
    public IReadOnlyList<Guid> PermissionIds { get; private set; } = [];

    public bool CanManageEmployees => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000004"));
    public bool CanManageCompany { get; private set; }

    // Where a plain employee (no manage permissions) should land when they're denied access to
    // an admin-only page, instead of the manager/HR-oriented dashboard. Falls back to the
    // dashboard for the rare case of a signed-in user with no linked employee record.
    public string MyProfileUrl => EmployeeId.HasValue
        ? $"/companies/{CompanyId}/employees/{EmployeeId}/profile"
        : "/";

    // Employee (null if user has no linked employee record)
    public Guid? EmployeeId { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? JobTitle { get; private set; }
    public WorkingDays? WorkingDaysOverride { get; private set; }
    public decimal? HoursPerDayOverride { get; private set; }
    public string? ProfileImageUrl { get; private set; }
    public string DisplayName => $"{FirstName} {LastName}".Trim() is { Length: > 0 } n ? n : Email ?? "Unknown";
    public string Initials => string.Concat(
        (FirstName?.Length > 0 ? FirstName[0].ToString() : ""),
        (LastName?.Length  > 0 ? LastName[0].ToString()  : "")).ToUpperInvariant()
        is { Length: > 0 } i ? i : "?";

    // Company
    public string CompanyName { get; private set; } = string.Empty;

    // Company settings
    public int WorkingDays { get; private set; }
    public decimal HoursPerDay { get; private set; }
    public int LeaveYearStartMonth { get; private set; }
    public decimal DefaultHolidayAllowance { get; private set; }
    public int ProbationMonths { get; private set; }
    public bool ExcludePublicHolidaysFromLeave { get; private set; }
    public bool DisplaySalaryOnEmployeeProfile { get; private set; }
    public string TimeZone { get; private set; } = "UTC";
    public string Locale { get; private set; } = "en-GB";
    public string? PostcodeRegex { get; private set; }
    public string? TelephoneRegex { get; private set; }
    public string? MobileRegex { get; private set; }

    // Company branding
    public string? PrimaryLogoUrl { get; private set; }
    public string? SmallLogoUrl { get; private set; }

    public async Task InitialiseAsync()
    {
        if (IsLoaded) return;

        MeResponse? me;
        try
        {
            me = await Http.GetFromJsonAsync<MeResponse>("api/me", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return;
        }

        if (me is null) return;

        UserId    = me.UserId;
        CompanyId = me.CompanyId;
        Email     = me.Email;
        PermissionIds = me.PermissionIds;
        CanManageCompany = me.CanManageCompany;

        var companyTask  = Http.GetFromJsonAsync<GetCompanyResponse>($"api/companies/{me.CompanyId}", HrApiJsonOptions.Default);
        var settingsTask = Http.GetFromJsonAsync<GetCompanySettingsResponse>($"api/companies/{me.CompanyId}/settings", HrApiJsonOptions.Default);
        var employeeTask = Http.GetFromJsonAsync<MyEmployeeResponse>($"api/companies/{me.CompanyId}/employees/me", HrApiJsonOptions.Default);

        await Task.WhenAll(companyTask, settingsTask, employeeTask);

        var company  = await companyTask;
        var settings = await settingsTask;
        var employee = await employeeTask;

        if (company is not null)
        {
            CompanyName     = company.Name;
            PrimaryLogoUrl  = company.Branding?.PrimaryLogoUrl;
            SmallLogoUrl    = company.Branding?.SmallLogoUrl;
        }

        if (settings is not null)
        {
            WorkingDays                  = settings.WorkingDays;
            HoursPerDay                  = settings.HoursPerDay;
            LeaveYearStartMonth          = settings.LeaveYearStartMonth;
            DefaultHolidayAllowance      = settings.DefaultHolidayAllowance;
            ProbationMonths              = settings.ProbationMonths;
            ExcludePublicHolidaysFromLeave = settings.ExcludePublicHolidaysFromLeave;
            DisplaySalaryOnEmployeeProfile = settings.DisplaySalaryOnEmployeeProfile;
            TimeZone                     = settings.TimeZone;
            Locale                       = settings.Locale;
            PostcodeRegex                = settings.PostcodeRegex;
            TelephoneRegex               = settings.TelephoneRegex;
            MobileRegex                  = settings.MobileRegex;
        }

        if (employee is not null)
        {
            EmployeeId          = employee.EmployeeId;
            FirstName           = employee.FirstName;
            LastName            = employee.LastName;
            JobTitle            = employee.JobTitle;
            WorkingDaysOverride = employee.WorkingDaysOverride;
            HoursPerDayOverride = employee.HoursPerDayOverride;
            ProfileImageUrl     = employee.ProfileImageUrl;
        }

        IsLoaded = true;
    }
}
