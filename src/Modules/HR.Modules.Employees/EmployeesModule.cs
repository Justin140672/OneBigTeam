using FluentValidation;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.AssignManager;
using HR.Modules.Employees.Features.CreateDepartment;
using HR.Modules.Employees.Features.CreateEmployee;
using HR.Modules.Employees.Features.CreatePositionProfile;
using HR.Modules.Employees.Features.DeactivateDepartment;
using HR.Modules.Employees.Features.GetEmployee;
using HR.Modules.Employees.Features.GetMyEmployee;
using HR.Modules.Employees.Features.ListDepartments;
using HR.Modules.Employees.Features.ListEmployees;
using HR.Modules.Employees.Features.SetEmployeeWorkingPattern;
using HR.Modules.Employees.Features.UpdateDepartment;
using HR.Modules.Employees.Features.GetPositionProfile;
using HR.Modules.Employees.Features.ListPositionProfiles;
using HR.Modules.Employees.Features.UpdateEmployeeProfile;
using HR.Modules.Employees.Features.UpdatePositionProfile;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Employees;

public static class EmployeesModule
{
    public static IServiceCollection AddEmployeesModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<EmployeesDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "employees")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<CreateDepartmentHandler>();
        services.AddScoped<IValidator<CreateDepartmentRequest>, CreateDepartmentValidator>();

        services.AddScoped<UpdateDepartmentHandler>();
        services.AddScoped<IValidator<UpdateDepartmentRequest>, UpdateDepartmentValidator>();

        services.AddScoped<DeactivateDepartmentHandler>();

        services.AddScoped<CreatePositionProfileHandler>();
        services.AddScoped<IValidator<CreatePositionProfileRequest>, CreatePositionProfileValidator>();

        services.AddScoped<GetPositionProfileHandler>();

        services.AddScoped<ListPositionProfilesHandler>();
        services.AddScoped<IValidator<ListPositionProfilesRequest>, ListPositionProfilesValidator>();

        services.AddScoped<UpdatePositionProfileHandler>();
        services.AddScoped<IValidator<UpdatePositionProfileRequest>, UpdatePositionProfileValidator>();

        services.AddScoped<CreateEmployeeHandler>();
        services.AddScoped<IValidator<CreateEmployeeRequest>, CreateEmployeeValidator>();

        services.AddScoped<GetEmployeeHandler>();
        services.AddScoped<GetMyEmployeeHandler>();

        services.AddScoped<ListDepartmentsHandler>();
        services.AddScoped<IValidator<ListDepartmentsRequest>, ListDepartmentsValidator>();

        services.AddScoped<ListEmployeesHandler>();
        services.AddScoped<IValidator<ListEmployeesRequest>, ListEmployeesValidator>();

        services.AddScoped<UpdateEmployeeProfileHandler>();
        services.AddScoped<IValidator<UpdateEmployeeProfileRequest>, UpdateEmployeeProfileValidator>();

        services.AddScoped<AssignManagerHandler>();
        services.AddScoped<IValidator<AssignManagerRequest>, AssignManagerValidator>();

        services.AddScoped<SetEmployeeWorkingPatternHandler>();
        services.AddScoped<IValidator<SetEmployeeWorkingPatternRequest>, SetEmployeeWorkingPatternValidator>();

        services.AddScoped<IWorkingPatternProvider, WorkingPatternProvider>();
        services.AddScoped<IDirectReportsReader, DirectReportsReader>();
        services.AddScoped<IEmployeeNameReader, EmployeeNameReader>();
        services.AddScoped<IManagerReader, ManagerReader>();
    }

    public static async Task MigrateEmployeesAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS employees");
        await db.Database.MigrateAsync();
    }

    public static async Task SeedEmployeesAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();

        var now = DateTimeOffset.UtcNow;

        // ── Acme Corporation ─────────────────────────────────────────────────
        var acmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        if (!await db.Employees.AnyAsync(e => e.CompanyId == acmeId))
        {
            var deptEngId      = Guid.Parse("10000000-0000-0000-0000-000000000001");
            var deptHrId       = Guid.Parse("10000000-0000-0000-0000-000000000002");
            var deptFinanceId  = Guid.Parse("10000000-0000-0000-0000-000000000003");
            var deptSalesId    = Guid.Parse("10000000-0000-0000-0000-000000000004");

            db.Departments.AddRange(
                Department.Create(deptEngId,     acmeId, "Engineering", "Product and platform engineering", now),
                Department.Create(deptHrId,      acmeId, "People & HR",  "HR and people operations",       now),
                Department.Create(deptFinanceId, acmeId, "Finance",       "Finance and accounting",         now),
                Department.Create(deptSalesId,   acmeId, "Sales",         "Sales and account management",   now));

            var posCtoId        = Guid.Parse("20000000-0000-0000-0000-000000000001");
            var posSenDevId     = Guid.Parse("20000000-0000-0000-0000-000000000002");
            var posDevId        = Guid.Parse("20000000-0000-0000-0000-000000000003");
            var posHrMgrId      = Guid.Parse("20000000-0000-0000-0000-000000000004");
            var posHrAdvisorId  = Guid.Parse("20000000-0000-0000-0000-000000000005");
            var posFinanceMgrId = Guid.Parse("20000000-0000-0000-0000-000000000006");
            var posSalesMgrId   = Guid.Parse("20000000-0000-0000-0000-000000000007");
            var posAeId         = Guid.Parse("20000000-0000-0000-0000-000000000008");

            db.PositionProfiles.AddRange(
                PositionProfile.Create(posCtoId,        acmeId, deptEngId,     "Chief Technology Officer", null, isManagerial: true,  now),
                PositionProfile.Create(posSenDevId,     acmeId, deptEngId,     "Senior Software Engineer", null, isManagerial: false, now),
                PositionProfile.Create(posDevId,        acmeId, deptEngId,     "Software Engineer",        null, isManagerial: false, now),
                PositionProfile.Create(posHrMgrId,      acmeId, deptHrId,      "HR Manager",               null, isManagerial: true,  now),
                PositionProfile.Create(posHrAdvisorId,  acmeId, deptHrId,      "HR Advisor",               null, isManagerial: false, now),
                PositionProfile.Create(posFinanceMgrId, acmeId, deptFinanceId, "Finance Manager",          null, isManagerial: true,  now),
                PositionProfile.Create(posSalesMgrId,   acmeId, deptSalesId,   "Sales Manager",            null, isManagerial: true,  now),
                PositionProfile.Create(posAeId,         acmeId, deptSalesId,   "Account Executive",        null, isManagerial: false, now));

            var empCtoId      = Guid.Parse("30000000-0000-0000-0000-000000000001");
            var empSenDev1Id  = Guid.Parse("30000000-0000-0000-0000-000000000002");
            var empSenDev2Id  = Guid.Parse("30000000-0000-0000-0000-000000000003");
            var empDev1Id     = Guid.Parse("30000000-0000-0000-0000-000000000004");
            var empHrMgrId    = Guid.Parse("30000000-0000-0000-0000-000000000005");
            var empHrAdvId    = Guid.Parse("30000000-0000-0000-0000-000000000006");
            var empFinMgrId   = Guid.Parse("30000000-0000-0000-0000-000000000007");
            var empSalesMgrId = Guid.Parse("30000000-0000-0000-0000-000000000008");
            var empAe1Id      = Guid.Parse("30000000-0000-0000-0000-000000000009");
            var empAe2Id      = Guid.Parse("30000000-0000-0000-0000-000000000010");

            Employee MakeAcme(Guid id, string first, string last, string email, DateOnly start,
                              Guid? deptId, Guid? posId, Guid? managerId)
            {
                var e = Employee.Create(id, acmeId, first, last, email, start, hasSystemAccess: true, now);
                e.Assign(deptId, posId, managerId, now);
                e.Activate(now);
                return e;
            }

            db.Employees.AddRange(
                MakeAcme(empCtoId,      "Sarah",  "Chen",     "sarah.chen@acme.example",     new DateOnly(2020, 1, 6),  deptEngId,     posCtoId,        null),
                MakeAcme(empSenDev1Id,  "James",  "Okafor",   "james.okafor@acme.example",   new DateOnly(2021, 3, 15), deptEngId,     posSenDevId,     empCtoId),
                MakeAcme(empSenDev2Id,  "Priya",  "Sharma",   "priya.sharma@acme.example",   new DateOnly(2021, 9, 1),  deptEngId,     posSenDevId,     empCtoId),
                MakeAcme(empDev1Id,     "Tom",    "Williams", "tom.williams@acme.example",   new DateOnly(2023, 2, 20), deptEngId,     posDevId,        empSenDev1Id),
                MakeAcme(empHrMgrId,    "Laura",  "Bennett",  "laura.bennett@acme.example",  new DateOnly(2019, 6, 3),  deptHrId,      posHrMgrId,      null),
                MakeAcme(empHrAdvId,    "Marcus", "Diallo",   "marcus.diallo@acme.example",  new DateOnly(2022, 11, 7), deptHrId,      posHrAdvisorId,  empHrMgrId),
                MakeAcme(empFinMgrId,   "Sophie", "Laurent",  "sophie.laurent@acme.example", new DateOnly(2020, 4, 14), deptFinanceId, posFinanceMgrId, null),
                MakeAcme(empSalesMgrId, "David",  "Park",     "david.park@acme.example",     new DateOnly(2018, 8, 22), deptSalesId,   posSalesMgrId,   null),
                MakeAcme(empAe1Id,      "Emma",   "Jones",    "emma.jones@acme.example",     new DateOnly(2023, 5, 2),  deptSalesId,   posAeId,         empSalesMgrId),
                MakeAcme(empAe2Id,      "Carlos", "Rivera",   "carlos.rivera@acme.example",  new DateOnly(2024, 1, 8),  deptSalesId,   posAeId,         empSalesMgrId));

            await db.SaveChangesAsync();
        }

        // ── Beta Corp ─────────────────────────────────────────────────────────
        var betaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        if (!await db.Employees.AnyAsync(e => e.CompanyId == betaCorpId))
        {
            var betaDeptEngId  = Guid.Parse("10000000-0000-0000-0000-000000000011");
            var betaPosEngMgrId = Guid.Parse("20000000-0000-0000-0000-000000000011");
            var betaPosDevId    = Guid.Parse("20000000-0000-0000-0000-000000000012");
            var betaEmpMgrId    = Guid.Parse("30000000-0000-0000-0000-000000000011");
            var betaEmpDevId    = Guid.Parse("30000000-0000-0000-0000-000000000012");

            db.Departments.Add(
                Department.Create(betaDeptEngId, betaCorpId, "Engineering", "Software engineering", now));

            db.PositionProfiles.AddRange(
                PositionProfile.Create(betaPosEngMgrId, betaCorpId, betaDeptEngId, "Engineering Manager", null, isManagerial: true,  now),
                PositionProfile.Create(betaPosDevId,    betaCorpId, betaDeptEngId, "Software Developer",  null, isManagerial: false, now));

            Employee MakeBeta(Guid id, string first, string last, string email, DateOnly start,
                              Guid? posId, Guid? managerId)
            {
                var e = Employee.Create(id, betaCorpId, first, last, email, start, hasSystemAccess: true, now);
                e.Assign(betaDeptEngId, posId, managerId, now);
                e.Activate(now);
                return e;
            }

            db.Employees.AddRange(
                MakeBeta(betaEmpMgrId, "Alice", "Morgan", "alice.morgan@betacorp.example", new DateOnly(2022, 3, 1), betaPosEngMgrId, null),
                MakeBeta(betaEmpDevId, "Bob",   "Taylor", "bob.taylor@betacorp.example",   new DateOnly(2023, 9, 4), betaPosDevId,    betaEmpMgrId));

            await db.SaveChangesAsync();
        }
    }
}
