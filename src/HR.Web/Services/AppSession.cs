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
    public string TimeZone { get; private set; } = "UTC";
    public string Locale { get; private set; } = "en-GB";

    // Company branding
    public string PrimaryColor { get; private set; } = "#000000";
    public string SecondaryColor { get; private set; } = "#6B7280";
    public string AccentColor { get; private set; } = "#3B82F6";
    public string? PrimaryLogoUrl { get; private set; }
    public string? SmallLogoUrl { get; private set; }

    public async Task InitialiseAsync()
    {
        if (IsLoaded) return;

        var me = await Http.GetFromJsonAsync<MeResponse>("api/me", HrApiJsonOptions.Default);
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
            PrimaryColor    = company.Branding?.PrimaryColor   ?? "#000000";
            SecondaryColor  = company.Branding?.SecondaryColor ?? "#6B7280";
            AccentColor     = company.Branding?.AccentColor    ?? "#3B82F6";
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
            TimeZone                     = settings.TimeZone;
            Locale                       = settings.Locale;
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
