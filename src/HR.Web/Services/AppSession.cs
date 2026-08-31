using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
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

    // ADM-05: permission-derived capability flags. These compute directly from the effective
    // PermissionIds fetched from api/me and are the authoritative UI gate for admin pages and
    // navigation (see specifications/product-specifications/30-administrative-role-separation-matrix.md).
    // Existing role-name flags below are kept for dashboard-routing/identity decisions only.
    public bool CanReadEmployees              => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000003"));
    public bool CanManageCompanyConfiguration => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000012"));
    public bool CanViewUsers                  => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000016"));
    public bool CanManageUsers                => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000017"));
    public bool CanManageHrSettings           => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000018"));
    public bool CanManageLeavePolicies        => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000022"));
    public bool CanManageSickness             => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000015"));
    public bool CanManageRecruitment          => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000026"));
    public bool CanViewCandidates             => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000028"));
    public bool CanViewReporting              => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000034"));
    public bool CanViewHrReports              => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000036"));
    public bool CanViewRecruitmentReports     => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000035"));
    public bool CanManageSharedDocuments      => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000030"));
    // ADM-02: consolidated Compliance Centre — HR Administrator only.
    public bool CanViewComplianceCentre       => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000043"));
    // ADM-03: administrative alerts & incidents inbox — HR Administrator only.
    public bool CanViewAdminAlerts            => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000044"));
    // ADM-08: administrative governance reporting hub — HR Administrator only.
    public bool CanViewGovernanceReporting    => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000045"));

    // ADM-05: shared access-denied outcome. Admin pages call this from OnBeforeLoadAsync/LoadAsync
    // instead of hand-rolling a redirect; when not allowed it bounces to the consistent
    // /access-denied page (replace: true so back-button doesn't re-trigger it) and returns false.
    public static bool GuardAccess(Microsoft.AspNetCore.Components.NavigationManager nav, bool allowed)
    {
        if (!allowed) nav.NavigateTo("/access-denied", replace: true);
        return allowed;
    }

    // Role-derived flags, additive to CanManageCompany/CanManageEmployees above — these drive
    // landing/nav/switcher decisions only. CanManageEmployees keeps gating existing widgets as-is.
    public bool IsHrAdministrator { get; private set; }
    public bool IsManager { get; private set; }
    public bool IsRecruiter { get; private set; }

    // True when the "Getting Started" onboarding checklist should be shown/landed-on for this
    // user — set from the company-onboarding checklist endpoint below. Only ever true for an HR
    // Administrator or Company Administrator (the only roles granted onboarding:view/manage);
    // defaults false on any fetch failure so a broken/absent endpoint never blocks sign-in.
    public bool ShowGettingStarted { get; private set; }

    // AppSession is initialised once per Blazor circuit (InteractiveServer keeps a persistent
    // SignalR circuit across in-app navigations), so ShowGettingStarted is otherwise only ever
    // refreshed once, at session init. Without this, a checklist that reaches 100% or gets
    // dismissed WHILE the current circuit is already live (i.e. GettingStarted.razor's own fetch
    // just persisted IsHidden=true server-side) would still show "Getting Started" in the nav
    // menu and still redirect back to it via LandingUrl until the user's next fresh login/reload
    // starts a brand-new circuit — call this the moment either happens so the current session's
    // cached flag matches reality immediately, not just on the next login.
    public void MarkGettingStartedHidden() => ShowGettingStarted = false;

    // True only for the initial Company Admin employee auto-created at signup, until they've
    // completed the "Complete Initial Employee Record on First Login" dialog. MainLayout blocks
    // the whole app shell behind that dialog while this is true.
    public bool RequiresInitialEmployeeSetup { get; private set; }

    // Mirrors MarkGettingStartedHidden's pattern — lets EmployeeCompletionDialog flip this back
    // to false immediately after a successful save, without needing a full session reload.
    public void MarkInitialEmployeeSetupComplete() => RequiresInitialEmployeeSetup = false;

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
            RequiresInitialEmployeeSetup = employee.RequiresInitialSetup;
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

    private async Task<IReadOnlyDictionary<Guid, string>> LoadSicknessCategoryNamesAsync()
    {
        var categories = (await sicknessCategoryService.ListSicknessCategoriesAsync(CompanyId))?.Items ?? [];
        return categories.ToDictionary(c => c.Id, c => c.Name);
    }
}
