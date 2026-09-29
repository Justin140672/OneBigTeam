using System.Reflection;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace HR.Architecture.Tests;

/// <summary>
/// Metadata-driven guardrails for tenant isolation (no EF global query filters / RLS exist, so
/// isolation depends on every entity carrying company_id and every handler constraining by it).
/// These tests are based on the EF model and endpoint/request types - not text search - and are
/// designed to FAIL when a developer adds a new entity or endpoint without making an explicit
/// tenant-isolation decision. See docs/architecture/tenant-isolation.md for the policy and the
/// documented exceptions.
/// </summary>
public class TenantIsolationGuardrailTests
{
    private static readonly string[] TenantKeyPropertyNames = ["CompanyId"];

    /// <summary>
    /// Entities that are deliberately NOT tenant-owned and have no tenant-owned parent, keyed by
    /// CLR type name (unique across the solution; a duplicate name in two modules must be listed
    /// once and is validated for both). Every entry states why it is safe.
    /// </summary>
    private static readonly Dictionary<string, string> GlobalEntities = new()
    {
        // Platform / cross-company reference data
        ["Company"] = "The tenant itself (tenants table); accessed by Id == route company only.",
        ["Nationality"] = "Global reference data shared by all tenants; read-only lookup.",
        ["Permission"] = "Global permission catalogue.",
        ["Role"] = "Global system role catalogue (roles are assigned per user; not tenant data).",
        ["RolePermission"] = "Global role-to-permission mapping.",
        ["PlatformAdministrator"] = "Platform admin identities (platform:admin surface only).",
        ["SessionRevocation"] = "Auth session revocation keyed by user id; consulted by middleware.",
        ["MarketingProduct"] = "Public marketing content (platform:admin authored).",
        ["MarketingFeature"] = "Public marketing content (platform:admin authored).",
        ["MarketingRoadmapItem"] = "Public marketing content (platform:admin authored).",
        ["PlatformMetricsSnapshot"] = "Platform-wide operational metrics (platform:admin surface only).",
        ["PlatformSettings"] = "Platform-wide settings singleton (platform:admin surface only).",
        ["HistoricalLeaveDeactivationRepairProgress"] = "Singleton maintenance-job progress row (cross-company job).",
        ["SupportAttachmentPendingDeletion"] = "Storage clean-up queue processed by a cross-company maintenance job.",

        // Identity join tables keyed by user/position id; every handler that reaches them first
        // proves the target user belongs to the route company (ITargetUserCompanyGuard /
        // employee lookups) - covered by cross-tenant integration tests.
        ["UserRole"] = "Join row keyed by user id; reached only after target-user company guard.",
        ["UserPosition"] = "Join row keyed by user id; reached only after target-user company guard.",
        ["PositionRole"] = "Join row keyed by position id; positions are tenant-owned.",
        ["ApplicationUser"] = "Legacy login identity keyed by employee id; reached after target-user company guard.",
        ["InvitationBatchRecipient"] = "Child of tenant-owned InvitationBatch (BatchId); reached via batch.CompanyId check.",


        // Child rows reached only via an already company-validated parent aggregate (parent
        // loaded with CompanyId, or CompanyId compared right after load). No FK is configured in
        // the model for these (plain Guid columns), so the model cannot prove the relationship.
    };

    /// <summary>Tenant-keyed entities whose CompanyId is legitimately nullable (reason each).</summary>
    private static readonly Dictionary<string, string> NullableCompanyIdAllowed = new()
    {
        ["ProcessedStripeEvent"] = "Stripe webhook de-duplication row; CompanyId is only known for events that map to a tenant.",
    };

    /// <summary>An exception entry may be keyed by "Name" or, to disambiguate, "DbContextName/Name".</summary>
    private static bool IsListed(Dictionary<string, string> list, Type ctxType, string entityName) =>
        list.ContainsKey(entityName) || list.ContainsKey($"{ctxType.Name}/{entityName}");

    private static IEnumerable<Type> DbContextTypes() =>
        TenantScopedRouteConventionTests.ModuleAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(DbContext).IsAssignableFrom(t)
                        && t.Namespace?.EndsWith(".Persistence", StringComparison.Ordinal) == true);

    private static DbContext Build(Type contextType)
    {
        var optionsType = typeof(DbContextOptions<>).MakeGenericType(contextType);
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(builderType)!;
        builder.UseNpgsql("Host=localhost");
        var options = builder.Options;
        Assert.IsAssignableFrom(optionsType, options);
        // Some contexts take optional extra services (e.g. ISensitiveDataProtector); pass null.
        var ctor = contextType.GetConstructors().OrderBy(c => c.GetParameters().Length).Last();
        var args = ctor.GetParameters().Select((p, i) => i == 0 ? options : null).ToArray();
        return (DbContext)ctor.Invoke(args);
    }

    private static IProperty? TenantKey(IEntityType entity) =>
        TenantKeyPropertyNames.Select(n => entity.FindProperty(n)).FirstOrDefault(p => p is not null);

    private static bool IsTenantOwned(IEntityType entity) => TenantKey(entity) is not null;

    private static bool HasTenantOwnedParent(IEntityType entity, HashSet<IEntityType>? seen = null)
    {
        seen ??= [];
        if (!seen.Add(entity)) return false;
        return entity.GetForeignKeys()
            .Where(fk => fk.IsRequired)
            .Select(fk => fk.PrincipalEntityType)
            .Any(p => IsTenantOwned(p) || HasTenantOwnedParent(p, seen));
    }

    [Fact]
    public void Every_Module_DbContext_Is_Discovered()
    {
        var contexts = DbContextTypes().ToList();
        Assert.True(contexts.Count >= 18,
            $"Expected to discover all module DbContexts; found {contexts.Count}: " +
            string.Join(", ", contexts.Select(c => c.Name)));
    }

    [Fact]
    public void Tenant_Owned_Entities_Have_Required_Guid_company_id_Column()
    {
        var violations = new List<string>();

        foreach (var ctxType in DbContextTypes())
        {
            using var ctx = Build(ctxType);
            foreach (var entity in ctx.Model.GetEntityTypes().Where(e => !e.IsOwned()))
            {
                var key = TenantKey(entity);
                if (key is null) continue;

                var table = StoreObjectIdentifier.Create(entity, StoreObjectType.Table);
                var column = table is null ? null : key.GetColumnName(table.Value);

                if (key.ClrType != typeof(Guid) && !NullableCompanyIdAllowed.ContainsKey(entity.ClrType.Name))
                    violations.Add($"{ctxType.Name}/{entity.ClrType.Name}: CompanyId must be a non-nullable Guid (was {key.ClrType.Name}).");
                if (column != "company_id")
                    violations.Add($"{ctxType.Name}/{entity.ClrType.Name}: CompanyId must map to column 'company_id' (was '{column}').");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// The core "new entity" guardrail: every entity in every module model must be classified as
    /// (a) tenant-owned (has CompanyId), (b) a required-FK child of a tenant-owned entity, or
    /// (c) explicitly listed in <see cref="GlobalEntities"/> or
    /// <see cref="ChildEntitiesWithoutModelledForeignKey"/> with a reason. Adding an entity with none of
    /// these fails the build and forces a tenant-isolation decision.
    /// </summary>
    [Fact]
    public void Every_Entity_Is_Classified_For_Tenant_Isolation()
    {
        var unclassified = new List<string>();

        foreach (var ctxType in DbContextTypes())
        {
            using var ctx = Build(ctxType);
            foreach (var entity in ctx.Model.GetEntityTypes().Where(e => !e.IsOwned()))
            {
                var name = entity.ClrType.Name;
                if (IsTenantOwned(entity)) continue;
                if (HasTenantOwnedParent(entity)) continue;
                if (IsListed(GlobalEntities, ctxType, name)) continue;
                if (IsListed(ChildEntitiesWithoutModelledForeignKey, ctxType, name)) continue;

                unclassified.Add($"{ctxType.Name}/{name}: no CompanyId, no required FK to a tenant-owned entity, " +
                                 "and not in the documented exception lists. Add a CompanyId (uuid NOT NULL, column " +
                                 "'company_id') or document the exception in docs/architecture/tenant-isolation.md and " +
                                 "TenantIsolationGuardrailTests.");
            }
        }

        Assert.True(unclassified.Count == 0,
            "Entities without a tenant-isolation classification:" + Environment.NewLine +
            string.Join(Environment.NewLine, unclassified));
    }

    [Fact]
    public void Exception_Lists_Contain_No_Stale_Entries()
    {
        var present = new HashSet<string>();
        foreach (var ctxType in DbContextTypes())
        {
            using var ctx = Build(ctxType);
            foreach (var e in ctx.Model.GetEntityTypes())
            {
                present.Add(e.ClrType.Name);
                present.Add($"{ctxType.Name}/{e.ClrType.Name}");
            }
        }

        var stale = GlobalEntities.Keys.Concat(ChildEntitiesWithoutModelledForeignKey.Keys)
            .Where(n => !present.Contains(n)).ToList();
        Assert.True(stale.Count == 0, "Stale exception entries (entity no longer exists): " + string.Join(", ", stale));

        var nowTenantOwned = new List<string>();
        foreach (var ctxType in DbContextTypes())
        {
            using var ctx = Build(ctxType);
            foreach (var e in ctx.Model.GetEntityTypes().Where(IsTenantOwned))
            {
                if (IsListed(GlobalEntities, ctxType, e.ClrType.Name) || IsListed(ChildEntitiesWithoutModelledForeignKey, ctxType, e.ClrType.Name))
                    nowTenantOwned.Add(e.ClrType.Name);
            }
        }
        Assert.True(nowTenantOwned.Count == 0,
            "Entities are tenant-owned (have CompanyId) but still listed as exceptions - remove them: " +
            string.Join(", ", nowTenantOwned));
    }

    /// <summary>
    /// Company-scoped endpoints must bind the route <c>{companyId}</c> to a <c>CompanyId</c>
    /// property on their request type so the handler can constrain by it. Endpoints listed in
    /// <see cref="RequestsWithoutCompanyId"/> take the tenant from the authenticated principal
    /// or otherwise never touch tenant data by resource id.
    /// </summary>
    [Fact]
    public void Company_Scoped_Endpoints_Expose_CompanyId_On_Their_Request()
    {
        var (routesByEndpoint, failures) = TenantScopedRouteConventionTests.InspectAll();
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        var violations = new List<string>();

        foreach (var (endpointType, routes) in routesByEndpoint)
        {
            if (!routes.Any(r => r.StartsWith("/api/companies/{companyId", StringComparison.OrdinalIgnoreCase)))
                continue;

            var requestType = RequestTypeOf(endpointType);
            // EndpointWithoutRequest / EmptyRequest endpoints read the route value directly in the endpoint
            // (Route<Guid>("companyId")); the request type cannot be inspected. Known limitation.
            if (requestType is null || requestType == typeof(EmptyRequest)) continue;

            var hasCompanyId = requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p => p.Name == "CompanyId" && p.PropertyType == typeof(Guid));

            if (!hasCompanyId && !RequestsWithoutCompanyId.ContainsKey(endpointType.FullName!))
                violations.Add($"{endpointType.FullName}: request '{requestType.Name}' has no Guid CompanyId, so " +
                               "the handler cannot constrain by the route company.");
        }

        Assert.True(violations.Count == 0,
            "Company-scoped endpoints whose request cannot carry the route company:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    private static Type? RequestTypeOf(Type endpointType)
    {
        for (var t = endpointType; t is not null && t != typeof(object); t = t.BaseType)
        {
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (def.Name.StartsWith("Endpoint`", StringComparison.Ordinal))
                    return t.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Child entities that reference a tenant-owned parent by a plain Guid column (no EF foreign
    /// key in the model) and are only ever loaded through a company-validated parent aggregate.
    /// </summary>
    private static readonly Dictionary<string, string> ChildEntitiesWithoutModelledForeignKey = new();

    /// <summary>Company-scoped endpoints whose request has no CompanyId (documented reason each).</summary>
    private static readonly Dictionary<string, string> RequestsWithoutCompanyId = new()
    {
        ["HR.Modules.Employees.Features.CompleteInitialEmployeeSetup.Endpoint"] =
            "Endpoint reads Route<string>(\"companyId\") and passes companyId to the handler explicitly.",
    };
}
