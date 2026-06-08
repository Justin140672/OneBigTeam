using FluentValidation;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.AssignManager;
using HR.Modules.Employees.Features.CreateDepartment;
using HR.Modules.Employees.Features.CreateEmployee;
using HR.Modules.Employees.Features.CreatePositionProfile;
using HR.Modules.Employees.Features.DeactivateDepartment;
using HR.Modules.Employees.Features.GetEmployee;
using HR.Modules.Employees.Features.ListDepartments;
using HR.Modules.Employees.Features.ListEmployees;
using HR.Modules.Employees.Features.UpdateDepartment;
using HR.Modules.Employees.Features.UpdateEmployeeProfile;
using HR.Modules.Employees.Persistence;
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

        services.AddScoped<CreateEmployeeHandler>();
        services.AddScoped<IValidator<CreateEmployeeRequest>, CreateEmployeeValidator>();

        services.AddScoped<GetEmployeeHandler>();

        services.AddScoped<ListDepartmentsHandler>();
        services.AddScoped<IValidator<ListDepartmentsRequest>, ListDepartmentsValidator>();

        services.AddScoped<ListEmployeesHandler>();
        services.AddScoped<IValidator<ListEmployeesRequest>, ListEmployeesValidator>();

        services.AddScoped<UpdateEmployeeProfileHandler>();
        services.AddScoped<IValidator<UpdateEmployeeProfileRequest>, UpdateEmployeeProfileValidator>();

        services.AddScoped<AssignManagerHandler>();
        services.AddScoped<IValidator<AssignManagerRequest>, AssignManagerValidator>();
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

        if (await db.Employees.AnyAsync())
            return;

        var now = DateTimeOffset.UtcNow;
        var companyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        // ── Departments ──────────────────────────────────────────────────────
        var deptEngId      = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var deptHrId       = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var deptFinanceId  = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var deptSalesId    = Guid.Parse("10000000-0000-0000-0000-000000000004");

        var deptEng     = Department.Create(deptEngId,     companyId, "Engineering", "Product and platform engineering", now);
        var deptHr      = Department.Create(deptHrId,      companyId, "People & HR",  "HR and people operations", now);
        var deptFinance = Department.Create(deptFinanceId, companyId, "Finance",       "Finance and accounting", now);
        var deptSales   = Department.Create(deptSalesId,   companyId, "Sales",         "Sales and account management", now);

        db.Departments.AddRange(deptEng, deptHr, deptFinance, deptSales);

        // ── Position Profiles ────────────────────────────────────────────────
        var posCtoId         = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var posSenDevId      = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var posDevId         = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var posHrMgrId       = Guid.Parse("20000000-0000-0000-0000-000000000004");
        var posHrAdvisorId   = Guid.Parse("20000000-0000-0000-0000-000000000005");
        var posFinanceMgrId  = Guid.Parse("20000000-0000-0000-0000-000000000006");
        var posSalesMgrId    = Guid.Parse("20000000-0000-0000-0000-000000000007");
        var posAeId          = Guid.Parse("20000000-0000-0000-0000-000000000008");

        db.PositionProfiles.AddRange(
            PositionProfile.Create(posCtoId,        companyId, deptEngId,     "Chief Technology Officer", null,                  isManagerial: true,  now),
            PositionProfile.Create(posSenDevId,     companyId, deptEngId,     "Senior Software Engineer", null,                  isManagerial: false, now),
            PositionProfile.Create(posDevId,        companyId, deptEngId,     "Software Engineer",        null,                  isManagerial: false, now),
            PositionProfile.Create(posHrMgrId,      companyId, deptHrId,      "HR Manager",               null,                  isManagerial: true,  now),
            PositionProfile.Create(posHrAdvisorId,  companyId, deptHrId,      "HR Advisor",               null,                  isManagerial: false, now),
            PositionProfile.Create(posFinanceMgrId, companyId, deptFinanceId, "Finance Manager",          null,                  isManagerial: true,  now),
            PositionProfile.Create(posSalesMgrId,   companyId, deptSalesId,   "Sales Manager",            null,                  isManagerial: true,  now),
            PositionProfile.Create(posAeId,         companyId, deptSalesId,   "Account Executive",        null,                  isManagerial: false, now));

        // ── Employees ────────────────────────────────────────────────────────
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

        Employee Make(Guid id, string first, string last, string email, DateOnly start,
                      Guid? deptId, Guid? posId, Guid? managerId)
        {
            var e = Employee.Create(id, companyId, first, last, email, start, now);
            e.Assign(deptId, posId, managerId, now);
            e.Activate(now);
            return e;
        }

        db.Employees.AddRange(
            Make(empCtoId,      "Sarah",  "Chen",     "sarah.chen@acme.example",     new DateOnly(2020, 1, 6),  deptEngId,     posCtoId,        null),
            Make(empSenDev1Id,  "James",  "Okafor",   "james.okafor@acme.example",   new DateOnly(2021, 3, 15), deptEngId,     posSenDevId,     empCtoId),
            Make(empSenDev2Id,  "Priya",  "Sharma",   "priya.sharma@acme.example",   new DateOnly(2021, 9, 1),  deptEngId,     posSenDevId,     empCtoId),
            Make(empDev1Id,     "Tom",    "Williams", "tom.williams@acme.example",   new DateOnly(2023, 2, 20), deptEngId,     posDevId,        empSenDev1Id),
            Make(empHrMgrId,    "Laura",  "Bennett",  "laura.bennett@acme.example",  new DateOnly(2019, 6, 3),  deptHrId,      posHrMgrId,      null),
            Make(empHrAdvId,    "Marcus", "Diallo",   "marcus.diallo@acme.example",  new DateOnly(2022, 11, 7), deptHrId,      posHrAdvisorId,  empHrMgrId),
            Make(empFinMgrId,   "Sophie", "Laurent",  "sophie.laurent@acme.example", new DateOnly(2020, 4, 14), deptFinanceId, posFinanceMgrId, null),
            Make(empSalesMgrId, "David",  "Park",     "david.park@acme.example",     new DateOnly(2018, 8, 22), deptSalesId,   posSalesMgrId,   null),
            Make(empAe1Id,      "Emma",   "Jones",    "emma.jones@acme.example",     new DateOnly(2023, 5, 2),  deptSalesId,   posAeId,         empSalesMgrId),
            Make(empAe2Id,      "Carlos", "Rivera",   "carlos.rivera@acme.example",  new DateOnly(2024, 1, 8),  deptSalesId,   posAeId,         empSalesMgrId));

        await db.SaveChangesAsync();
    }
}
