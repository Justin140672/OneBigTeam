using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class AppSession(IHttpClientFactory httpClientFactory, EmployeeService employeeService, SicknessCategoryService sicknessCategoryService)
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

    // Role-derived flags, additive to CanManageCompany/CanManageEmployees above — these drive
    // landing/nav/switcher decisions only. CanManageEmployees keeps gating existing widgets as-is.
    public bool IsHrAdministrator { get; private set; }
    public bool IsManager { get; private set; }
    public bool IsRecruiter { get; private set; }

    // Where a plain employee (no manage permissions) should land when they're denied access to
    // an admin-only page, instead of the manager/HR-oriented dashboard. Falls back to the
    // dashboard for the rare case of a signed-in user with no linked employee record.
    public string MyProfileUrl => EmployeeId.HasValue
        ? $"/companies/{CompanyId}/employees/{EmployeeId}/profile"
        : "/";

    // Priority order for where a signed-in user lands on "/": HR Administrator > Recruiter >
    // Manager > Company Administrator (no HR role) > plain Employee's own profile. Consumed by
    // Home.razor as the fallback when there's no (or no longer valid) localStorage selection.
    public string LandingUrl =>
        IsHrAdministrator ? "/dashboard/hr" :
        IsRecruiter ? "/dashboard/recruitment" :
        IsManager ? "/dashboard/manager" :
        CanManageCompany ? $"/companies/{CompanyId}/edit" :
        MyProfileUrl;

    // Dashboard keys used by routing/localStorage: "hr", "recruitment", "manager".
    public bool IsDashboardAvailable(string dashboardKey) => dashboardKey switch
    {
        "hr" => IsHrAdministrator,
        "recruitment" => IsRecruiter,
        "manager" => IsManager,
        _ => false,
    };

    public static string? DashboardUrl(string dashboardKey) => dashboardKey switch
    {
        "hr" => "/dashboard/hr",
        "recruitment" => "/dashboard/recruitment",
        "manager" => "/dashboard/manager",
        _ => null,
    };

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
        IsHrAdministrator = me.IsHrAdministrator;
        IsManager = me.IsManager;
        IsRecruiter = me.IsRecruiter;

        var companyTask  = Http.GetFromJsonAsync<GetCompanyResponse>($"api/companies/{me.CompanyId}", HrApiJsonOptions.Default);
        var settingsTask = Http.GetFromJsonAsync<GetCompanySettingsResponse>($"api/companies/{me.CompanyId}/settings", HrApiJsonOptions.Default);
        var employeeTask = GetEmployeeOrNullAsync(me.CompanyId);

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

    // A signed-in user isn't always linked to an Employee record (e.g. a Company Administrator
    // account with no employee profile, like the "just company admin" persona) — MyProfileUrl
    // and every EmployeeId-derived property above already treat a null EmployeeId as "no linked
    // employee", so a 404 here is an expected outcome, not a fatal one. Unlike the other two
    // fetches in InitialiseAsync's Task.WhenAll, this one must not let an HttpRequestException
    // propagate and take down the whole session load.
    private async Task<MyEmployeeResponse?> GetEmployeeOrNullAsync(Guid companyId)
    {
        try
        {
            return await Http.GetFromJsonAsync<MyEmployeeResponse>(
                $"api/companies/{companyId}/employees/me", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // Several dashboard widgets each need an employee id -> display name lookup (and a couple
    // need sickness category names). Cached per-circuit here so they share one fetch instead of
    // each independently re-requesting the full employee/category list.
    private Task<IReadOnlyDictionary<Guid, string>>? _employeeNamesTask;
    private Task<IReadOnlyDictionary<Guid, string>>? _sicknessCategoryNamesTask;

    public Task<IReadOnlyDictionary<Guid, string>> GetEmployeeNamesAsync() =>
        _employeeNamesTask ??= LoadEmployeeNamesAsync();

    private async Task<IReadOnlyDictionary<Guid, string>> LoadEmployeeNamesAsync()
    {
        var employees = (await employeeService.ListEmployeesAsync(CompanyId, pageSize: 200))?.Items ?? [];
        return employees.ToDictionary(e => e.Id, e => $"{e.FirstName} {e.LastName}");
    }

    public Task<IReadOnlyDictionary<Guid, string>> GetSicknessCategoryNamesAsync() =>
        _sicknessCategoryNamesTask ??= LoadSicknessCategoryNamesAsync();

    private async Task<IReadOnlyDictionary<Guid, string>> LoadSicknessCategoryNamesAsync()
    {
        var categories = (await sicknessCategoryService.ListSicknessCategoriesAsync(CompanyId))?.Items ?? [];
        return categories.ToDictionary(c => c.Id, c => c.Name);
    }
}
