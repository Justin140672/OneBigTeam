using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class AppSession(IHttpClientFactory httpClientFactory, EmployeeService employeeService, SicknessCategoryService sicknessCategoryService, CompanyOnboardingService companyOnboardingService, SubscriptionService subscriptionService)
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

    // Interim stub for the real Supabase-Auth email-confirmation flow (out of scope for now — see
    // ApplicationUser.IsEmailConfirmed remarks). Only self-service SignUp accounts start
    // unconfirmed; every other creation path defaults to already-confirmed. False blocks the
    // entire app behind EmailConfirmationRequired.razor (see MainLayout.razor).
    public bool IsEmailConfirmed { get; private set; } = true;

    // True when the "Getting Started" onboarding checklist should be shown/landed-on for this
    // user — set from the company-onboarding checklist endpoint below. Only ever true for an HR
    // Administrator or Company Administrator (the only roles granted onboarding:view/manage);
    // defaults false on any fetch failure so a broken/absent endpoint never blocks sign-in.
    public bool ShowGettingStarted { get; private set; }

    // Trial/subscription status (Getting Started + Subscription/Billing epic, Phase B) — drives
    // TrialBanner visibility and (in a later phase) read-only UI gating. Defaults to a
    // non-blocking "Active" status on any fetch failure, same fail-open convention as
    // ShowGettingStarted above, so a broken/absent endpoint never blocks sign-in.
    public SubscriptionStatus SubscriptionStatus { get; private set; } = SubscriptionStatus.Active;
    public int TrialDaysRemaining { get; private set; }
    public bool IsReadOnly { get; private set; }

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
        ShowGettingStarted && (IsHrAdministrator || CanManageCompany) ? "/getting-started" :
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
        IsEmailConfirmed = me.IsEmailConfirmed;
        IsManager = me.IsManager;
        IsRecruiter = me.IsRecruiter;

        var companyTask    = GetCompanyOrNullAsync(me.CompanyId);
        var settingsTask   = GetCompanySettingsOrNullAsync(me.CompanyId);
        var hrSettingsTask = GetHrSettingsOrNullAsync(me.CompanyId);
        var employeeTask   = GetEmployeeOrNullAsync(me.CompanyId);

        // Only HR Administrators / Company Administrators are granted the onboarding:view policy;
        // everyone else gets a 403, so skip the call entirely rather than let it noisily fail.
        var onboardingTask = me.IsHrAdministrator || me.CanManageCompany
            ? GetOnboardingChecklistOrNullAsync()
            : Task.FromResult<GetCompanyOnboardingChecklistResponse?>(null);
        var subscriptionTask = GetSubscriptionStatusOrNullAsync();

        await Task.WhenAll(companyTask, settingsTask, hrSettingsTask, employeeTask, onboardingTask, subscriptionTask);

        var company      = await companyTask;
        var settings     = await settingsTask;
        var hrSettings   = await hrSettingsTask;
        var employee     = await employeeTask;
        var onboarding   = await onboardingTask;
        var subscription = await subscriptionTask;

        ShowGettingStarted = onboarding is not null && !onboarding.IsHidden && !onboarding.IsDismissedEarly;

        if (subscription is not null)
        {
            SubscriptionStatus = subscription.Status;
            TrialDaysRemaining = subscription.TrialDaysRemaining;
            IsReadOnly = subscription.IsReadOnly;
        }

        if (company is not null)
        {
            CompanyName     = company.Name;
            PrimaryLogoUrl  = company.Branding?.PrimaryLogoUrl;
            SmallLogoUrl    = company.Branding?.SmallLogoUrl;
        }

        if (settings is not null)
        {
            TimeZone                     = settings.TimeZone;
            Locale                       = settings.Locale;
            PostcodeRegex                = settings.PostcodeRegex;
            TelephoneRegex               = settings.TelephoneRegex;
            MobileRegex                  = settings.MobileRegex;
        }

        if (hrSettings is not null)
        {
            WorkingDays                  = hrSettings.WorkingDays;
            HoursPerDay                  = hrSettings.HoursPerDay;
            LeaveYearStartMonth          = hrSettings.LeaveYearStartMonth;
            DefaultHolidayAllowance      = hrSettings.DefaultHolidayAllowance;
            ProbationMonths              = hrSettings.ProbationMonths;
            ExcludePublicHolidaysFromLeave = hrSettings.ExcludePublicHolidaysFromLeave;
            DisplaySalaryOnEmployeeProfile = hrSettings.DisplaySalaryOnEmployeeProfile;
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

    // Mirrors GetEmployeeOrNullAsync's guard above — a permission gap (e.g. a role missing the
    // "role:employee" floor) or any other transient failure must not take down session
    // initialisation and crash the Blazor Server circuit; the relevant session fields simply keep
    // their fail-open defaults in that case.
    private async Task<GetCompanyResponse?> GetCompanyOrNullAsync(Guid companyId)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetCompanyResponse>(
                $"api/companies/{companyId}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<GetCompanySettingsResponse?> GetCompanySettingsOrNullAsync(Guid companyId)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetCompanySettingsResponse>(
                $"api/companies/{companyId}/settings", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<GetHrSettingsResponse?> GetHrSettingsOrNullAsync(Guid companyId)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetHrSettingsResponse>(
                $"api/companies/{companyId}/hr-settings", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // Mirrors GetEmployeeOrNullAsync's guard above: a 403 (permission not granted after all) or
    // any other transient failure must not take down session initialisation — ShowGettingStarted
    // simply defaults to false in that case.
    private async Task<GetCompanyOnboardingChecklistResponse?> GetOnboardingChecklistOrNullAsync()
    {
        try
        {
            return await companyOnboardingService.GetChecklistAsync();
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // Mirrors GetOnboardingChecklistOrNullAsync's guard above — a transient failure must not take
    // down session initialisation; SubscriptionStatus/TrialDaysRemaining/IsReadOnly simply keep
    // their fail-open defaults in that case.
    private async Task<GetSubscriptionStatusResponse?> GetSubscriptionStatusOrNullAsync()
    {
        try
        {
            return await subscriptionService.GetStatusAsync();
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

    // Optimistic local update after EmailConfirmationRequired's dev-only confirm call succeeds —
    // avoids a full session reload just to flip one flag.
    public void SetEmailConfirmed() => IsEmailConfirmed = true;

    private async Task<IReadOnlyDictionary<Guid, string>> LoadSicknessCategoryNamesAsync()
    {
        var categories = (await sicknessCategoryService.ListSicknessCategoriesAsync(CompanyId))?.Items ?? [];
        return categories.ToDictionary(c => c.Id, c => c.Name);
    }
}
