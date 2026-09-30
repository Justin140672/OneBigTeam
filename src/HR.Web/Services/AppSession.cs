using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Web.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace HR.Web.Services;

public sealed class AppSession(HrApiHttpClientFactory httpClientFactory, EmployeeService employeeService, SicknessCategoryService sicknessCategoryService, CompanyOnboardingService companyOnboardingService, SubscriptionService subscriptionService, CircuitSessionState sessionState, AuthenticationStateProvider authStateProvider, NavigationManager navigationManager)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public bool IsLoaded { get; private set; }

    public Guid UserId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string? Email { get; private set; }
    public IReadOnlyList<Guid> PermissionIds { get; private set; } = [];

    public bool CanManageEmployees => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000004"));
    public bool CanManageCompany { get; private set; }

    public bool CanReadEmployees              => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000003"));
    public bool CanManageCompanyConfiguration => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000012"));
    public bool CanViewUsers                  => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000016"));
    public bool CanManageUsers                => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000017"));
    public bool CanManageHrSettings           => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000018"));
    public bool CanManageLeavePolicies        => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000022"));
    public bool CanManageSickness             => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000015"));
    public bool CanManageRecruitment          => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000026"));
    // Ticket 7 removed the redundant candidate.view permission (id ...028); candidate access is
    // recruitment:manage (Recruiter). Checking the deleted id made every user "no access".
    public bool CanViewCandidates             => CanManageRecruitment;
    public bool CanViewReporting              => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000034"));
    public bool CanViewHrReports              => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000036"));
    public bool CanViewRecruitmentReports     => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000035"));
    public bool CanViewEqualityReports        => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000046"));
    public bool CanManageSharedDocuments      => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000030"));
    public bool CanViewCompliance             => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000043"));

    // OBT-IAM-09: Getting Started and Support Requests must be gated on their own explicit
    // permissions (onboarding:view, support:manage), not on CanManageCompany — a
    // Company-Administrator-only account no longer holds either grant (see
    // RolePermissionConfiguration), so it must not see or reach either destination. HR
    // Administrator (and any Company Administrator who also holds HR Administrator) still holds
    // both via its own RolePermission grants.
    public bool CanViewOnboarding             => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000019"));
    public bool CanManageSupport              => PermissionIds.Contains(new Guid("00000000-0000-0000-0001-000000000042"));

    public static bool GuardAccess(Microsoft.AspNetCore.Components.NavigationManager nav, bool allowed)
    {
        if (!allowed) nav.NavigateTo("/access-denied", replace: true);
        return allowed;
    }

    public bool IsHrAdministrator { get; private set; }
    public bool IsManager { get; private set; }
    public bool IsRecruiter { get; private set; }

    public bool ShowGettingStarted { get; private set; }

    public void MarkGettingStartedHidden() => ShowGettingStarted = false;

    public bool RequiresInitialEmployeeSetup { get; private set; }

    public void MarkInitialEmployeeSetupComplete() => RequiresInitialEmployeeSetup = false;

    public SubscriptionResolution SubscriptionResolution { get; private set; } = SubscriptionResolution.Unresolved;
    public SubscriptionStatus SubscriptionStatus { get; private set; } = SubscriptionStatus.Active;
    public int TrialDaysRemaining { get; private set; }
    public bool IsReadOnly { get; private set; }

    public MainLayoutMode LayoutMode =>
        MainLayoutModeResolver.Resolve(IsLoaded, SubscriptionResolution, IsReadOnly, RequiresInitialEmployeeSetup);

    public void ApplySubscriptionStatus(GetSubscriptionStatusResponse? subscription)
    {
        if (subscription is null) return;
        SetSubscription(subscription);
        Changed?.Invoke();
    }

    private void SetSubscription(GetSubscriptionStatusResponse subscription)
    {
        SubscriptionStatus = subscription.Status;
        TrialDaysRemaining = subscription.TrialDaysRemaining;
        IsReadOnly = subscription.IsReadOnly;
        SubscriptionResolution = SubscriptionResolution.Resolved;
    }

    private void ResetSubscription()
    {
        SubscriptionResolution = SubscriptionResolution.Unresolved;
        SubscriptionStatus = SubscriptionStatus.Active;
        TrialDaysRemaining = 0;
        IsReadOnly = false;
    }

    public string MyProfileUrl => EmployeeId.HasValue
        ? $"/companies/{CompanyId}/employees/{EmployeeId}/profile"
        : "/";

    public string LandingUrl =>
        ShowGettingStarted && (IsHrAdministrator || CanViewOnboarding) ? "/getting-started" :
        IsHrAdministrator ? "/dashboard/hr" :
        IsRecruiter ? "/dashboard/recruitment" :
        IsManager ? "/dashboard/manager" :
        CanManageCompany ? $"/companies/{CompanyId}/edit" :
        MyProfileUrl;

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

    public string CompanyName { get; private set; } = string.Empty;

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

    public string? PrimaryLogoUrl { get; private set; }
    public string? SmallLogoUrl { get; private set; }

    public event Action? Changed;

    private Task? _inFlight;

    // Ticket 12: the Supabase access token this session's cached identity/permissions/employee
    // data was actually loaded for. AppSessionAuthStateProvider.SetAuthenticationState is
    // authoritative for the circuit's CURRENT auth state (it fail-closes CircuitSessionState on an
    // anonymous or different-identity reconnect — see that class's remarks), but this cache
    // (IsLoaded + all the fields below) previously had no way to notice that had happened, so a
    // previous user's cached data could silently keep being served for the rest of the circuit's
    // life even after the circuit's auth state had already been correctly blocked. Comparing
    // against the live CircuitSessionState below forces a real reload whenever the identity this
    // cache was built for is no longer the circuit's current (or has been invalidated).
    private string? _loadedForToken;

    private bool _authChangeSubscribed;

    private void EnsureSubscribedToAuthChanges()
    {
        if (_authChangeSubscribed) return;
        _authChangeSubscribed = true;
        authStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task) => _ = HandleAuthenticationStateChangedAsync(task);

    private async Task HandleAuthenticationStateChangedAsync(Task<AuthenticationState> task)
    {
        AuthenticationState state;
        try
        {
            state = await task;
        }
        catch
        {
            return;
        }

        // Only react to a transition INTO anonymous on a circuit that was previously loaded — an
        // anonymous notification before this session ever loaded (e.g. the initial seed on a fresh
        // circuit) is not a stale-identity condition and must not trigger a redirect loop on /login
        // itself (which uses a different layout and never calls InitialiseAsync in the first place,
        // but defend here too since this subscription is intentionally circuit-lifetime-long).
        if (state.User.Identity?.IsAuthenticated == true) return;
        if (!IsLoaded) return;

        IsLoaded = false;
        _inFlight = null;
        UserId = default;
        CompanyId = default;
        Email = null;
        PermissionIds = [];
        EmployeeId = null;
        FirstName = null;
        LastName = null;
        JobTitle = null;
        ProfileImageUrl = null;
        RequiresInitialEmployeeSetup = false;
        ResetSubscription();

        Changed?.Invoke();
        navigationManager.NavigateTo("/login", forceLoad: true);
    }

    public async Task InitialiseAsync()
    {
        EnsureSubscribedToAuthChanges();

        if (IsLoaded)
        {
            if (sessionState.Status == CircuitAuthStatus.Invalidated || sessionState.AccessToken != _loadedForToken)
            {
                IsLoaded = false;
                _inFlight = null;
                RequiresInitialEmployeeSetup = false;
                ResetSubscription();
            }
            else
            {
                return;
            }
        }

        _inFlight ??= LoadIdentityAsync();
        try
        {
            await _inFlight;
        }
        finally
        {
            if (!IsLoaded) _inFlight = null;
        }

        Changed?.Invoke();

        if (IsLoaded)
            _ = LoadEnrichmentAsync();
    }

    private async Task LoadIdentityAsync()
    {
        if (IsLoaded) return;

        MeResponse? me = null;
        for (var attempt = 1; attempt <= 4 && me is null; attempt++)
        {
            try
            {
                me = await Http.GetFromJsonAsync<MeResponse>("api/me", HrApiJsonOptions.Default);
            }
            catch
            {
                if (attempt < 4)
                    await Task.Delay(400 * attempt);
            }
        }

        if (me is null) return;

        ResetSubscription();
        RequiresInitialEmployeeSetup = false;

        UserId    = me.UserId;
        CompanyId = me.CompanyId;
        Email     = me.Email;
        PermissionIds = me.PermissionIds;
        CanManageCompany = me.CanManageCompany;
        IsHrAdministrator = me.IsHrAdministrator;
        IsManager = me.IsManager;
        IsRecruiter = me.IsRecruiter;

        // Employee data (EmployeeId above all) stays in this fast, blocking phase rather than
        // moving to the background LoadEnrichmentAsync below — several pages (MyProfile/
        // MyProfileOverviewTab's own "you can only view your own profile" guard, chief among them)
        // read Session.EmployeeId for an authorization decision on their very first render, and
        // none of those pages subscribe to Session.Changed to correct themselves later, so a null
        // EmployeeId at that first check would wrongly and permanently deny access for the whole
        // page's lifetime. This is one single call (not the six-way fan-out below), so it doesn't
        // reintroduce the multi-call blocking problem LoadEnrichmentAsync exists to avoid.
        // The subscription status is resolved in this same blocking phase (in parallel with the
        // employee fetch) so MainLayout never has to guess read-only state while deciding whether
        // to show the Complete Profile dialog.
        var employeeTask = GetEmployeeOrNullAsync(me.CompanyId);
        var subscriptionTask = ResolveSubscriptionAsync();
        await Task.WhenAll(employeeTask, subscriptionTask);

        var employee = await employeeTask;
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
        _loadedForToken = sessionState.AccessToken;
    }

    private async Task LoadEnrichmentAsync()
    {
        try
        {
            var companyTask    = GetCompanyOrNullAsync(CompanyId);
            var settingsTask   = GetCompanySettingsOrNullAsync(CompanyId);
            var hrSettingsTask = GetHrSettingsOrNullAsync(CompanyId);

            var onboardingTask = IsHrAdministrator || CanViewOnboarding
                ? GetOnboardingChecklistOrNullAsync()
                : Task.FromResult<GetCompanyOnboardingChecklistResponse?>(null);

            await Task.WhenAll(companyTask, settingsTask, hrSettingsTask, onboardingTask);

            var company      = await companyTask;
            var settings     = await settingsTask;
            var hrSettings   = await hrSettingsTask;
            var onboarding   = await onboardingTask;

            ShowGettingStarted = onboarding is not null && !onboarding.IsHidden && !onboarding.IsDismissedEarly;

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
        }
        catch
        {
        }

        Changed?.Invoke();
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

    private const int SubscriptionFetchAttempts = 3;

    private async Task ResolveSubscriptionAsync()
    {
        GetSubscriptionStatusResponse? subscription = null;
        for (var attempt = 1; attempt <= SubscriptionFetchAttempts && subscription is null; attempt++)
        {
            subscription = await GetSubscriptionStatusOrNullAsync();
            if (subscription is null && attempt < SubscriptionFetchAttempts)
                await Task.Delay(100 * attempt);
        }

        if (subscription is null)
        {
            SubscriptionResolution = SubscriptionResolution.Failed;
            return;
        }

        SetSubscription(subscription);
    }

    private async Task<GetSubscriptionStatusResponse?> GetSubscriptionStatusOrNullAsync()
    {
        try
        {
            return await subscriptionService.GetStatusAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or NotSupportedException or TaskCanceledException)
        {
            return null;
        }
    }

    private Task<IReadOnlyDictionary<Guid, string>>? _employeeNamesTask;
    private Task<IReadOnlyDictionary<Guid, string>>? _sicknessCategoryNamesTask;

    public Task<IReadOnlyDictionary<Guid, string>> GetEmployeeNamesAsync() =>
        _employeeNamesTask ??= LoadEmployeeNamesAsync();

    private async Task<IReadOnlyDictionary<Guid, string>> LoadEmployeeNamesAsync()
    {
        var employees = (await employeeService.ListSelectableEmployeesAsync(CompanyId, pageSize: 200))?.Items ?? [];
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
