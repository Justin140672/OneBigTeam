using FluentValidation;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.ApproveLeaveRequest;
using HR.Modules.Leave.Features.AssignLeavePolicyToEmployee;
using HR.Modules.Leave.Features.CancelLeaveRequest;
using HR.Modules.Leave.Features.CreatePublicHoliday;
using HR.Modules.Leave.Features.ListPublicHolidays;
using HR.Modules.Leave.Features.UpdatePublicHoliday;
using HR.Modules.Leave.Features.RejectLeaveRequest;
using HR.Modules.Leave.Features.CreateLeavePolicy;
using HR.Modules.Leave.Features.UpdateLeavePolicy;
using HR.Modules.Leave.Features.GetEmployeeLeaveBalance;
using HR.Modules.Leave.Features.GetLeavePolicy;
using HR.Modules.Leave.Features.ListLeavePolicies;
using HR.Modules.Leave.Features.ListLeaveRequests;
using HR.Modules.Leave.Features.SubmitLeaveRequest;
using HR.Modules.Leave.Features.PreviewLeaveRequest;
using HR.Modules.Leave.Features.AwardToil;
using HR.Modules.Leave.Features.InitialiseEmployeeLeave;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.SharedKernel;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Leave;

public static class LeaveModule
{
    public static IServiceCollection AddLeaveModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<LeaveDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "leave")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<CreateLeavePolicyHandler>();
        services.AddScoped<IValidator<CreateLeavePolicyRequest>, CreateLeavePolicyValidator>();
        services.AddScoped<UpdateLeavePolicyHandler>();
        services.AddScoped<IValidator<UpdateLeavePolicyRequest>, UpdateLeavePolicyValidator>();
        services.AddScoped<GetLeavePolicyHandler>();
        services.AddScoped<ListLeavePoliciesHandler>();
        services.AddScoped<AssignLeavePolicyToEmployeeHandler>();
        services.AddScoped<IValidator<AssignLeavePolicyToEmployeeRequest>, AssignLeavePolicyToEmployeeValidator>();
        services.AddScoped<GetEmployeeLeaveBalanceHandler>();
        services.AddScoped<IValidator<GetEmployeeLeaveBalanceRequest>, GetEmployeeLeaveBalanceValidator>();
        services.AddScoped<ListLeaveRequestsHandler>();
        services.AddScoped<SubmitLeaveRequestHandler>();
        services.AddScoped<IValidator<SubmitLeaveRequestRequest>, SubmitLeaveRequestValidator>();
        services.AddScoped<PreviewLeaveRequestHandler>();
        services.AddScoped<IValidator<PreviewLeaveRequestRequest>, PreviewLeaveRequestValidator>();
        services.AddScoped<CancelLeaveRequestHandler>();
        services.AddScoped<IValidator<CancelLeaveRequestRequest>, CancelLeaveRequestValidator>();
        services.AddScoped<ApproveLeaveRequestHandler>();
        services.AddScoped<IValidator<ApproveLeaveRequestRequest>, ApproveLeaveRequestValidator>();
        services.AddScoped<RejectLeaveRequestHandler>();
        services.AddScoped<IValidator<RejectLeaveRequestRequest>, RejectLeaveRequestValidator>();
        services.AddScoped<AwardToilHandler>();
        services.AddScoped<IValidator<AwardToilRequest>, AwardToilValidator>();
services.AddScoped<IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>, EmployeeCreatedHandler>();
        services.AddScoped<ILeaveApprovalService, LeaveApprovalService>();
        services.AddScoped<CreatePublicHolidayHandler>();
        services.AddScoped<IValidator<CreatePublicHolidayRequest>, CreatePublicHolidayValidator>();
        services.AddScoped<ListPublicHolidaysHandler>();
        services.AddScoped<UpdatePublicHolidayHandler>();
        services.AddScoped<IValidator<UpdatePublicHolidayRequest>, UpdatePublicHolidayValidator>();
    }

    public static async Task MigrateLeaveAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS leave");
        await db.Database.MigrateAsync();
    }

    public static async Task SeedLeaveAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var now = DateTimeOffset.UtcNow;
        var companyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        if (!await db.LeaveTypes.AnyAsync())
        {
            db.LeaveTypes.AddRange(
                LeaveType.Create(Guid.Parse("A0000000-0000-0000-0000-000000000001"), companyId, "Annual Leave",        "ANNUAL",        25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now),
                LeaveType.Create(Guid.Parse("A0000000-0000-0000-0000-000000000002"), companyId, "Sick Leave",          "SICK",          10, AccrualMethod.None,    LeaveTypeBehaviour.Sickness,  now),
                LeaveType.Create(Guid.Parse("A0000000-0000-0000-0000-000000000003"), companyId, "Unpaid Leave",        "UNPAID",         0, AccrualMethod.None,    LeaveTypeBehaviour.Unpaid,    now),
                LeaveType.Create(Guid.Parse("A0000000-0000-0000-0000-000000000004"), companyId, "Compassionate Leave", "COMPASSIONATE",  5, AccrualMethod.None,    LeaveTypeBehaviour.Standard,  now),
                LeaveType.Create(Guid.Parse("A0000000-0000-0000-0000-000000000005"), companyId, "Parental Leave",      "PARENTAL",      52, AccrualMethod.None,    LeaveTypeBehaviour.Parental,  now),
                LeaveType.Create(Guid.Parse("A0000000-0000-0000-0000-000000000006"), companyId, "Time Off In Lieu",    "TOIL",           0, AccrualMethod.None,    LeaveTypeBehaviour.Toil,      now));

            await db.SaveChangesAsync();
        }

        if (!await db.PublicHolidays.AnyAsync())
        {
            db.PublicHolidays.AddRange(
                // 2025 — England & Wales
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000101"), companyId, new DateOnly(2025,  1,  1), "New Year's Day",              "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000102"), companyId, new DateOnly(2025,  4, 18), "Good Friday",                 "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000103"), companyId, new DateOnly(2025,  4, 21), "Easter Monday",               "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000104"), companyId, new DateOnly(2025,  5,  5), "Early May Bank Holiday",      "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000105"), companyId, new DateOnly(2025,  5, 26), "Spring Bank Holiday",         "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000106"), companyId, new DateOnly(2025,  8, 25), "Summer Bank Holiday",         "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000107"), companyId, new DateOnly(2025, 12, 25), "Christmas Day",               "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000108"), companyId, new DateOnly(2025, 12, 26), "Boxing Day",                  "GB", now),

                // 2026 — England & Wales
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000201"), companyId, new DateOnly(2026,  1,  1), "New Year's Day",              "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000202"), companyId, new DateOnly(2026,  4,  3), "Good Friday",                 "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000203"), companyId, new DateOnly(2026,  4,  6), "Easter Monday",               "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000204"), companyId, new DateOnly(2026,  5,  4), "Early May Bank Holiday",      "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000205"), companyId, new DateOnly(2026,  5, 25), "Spring Bank Holiday",         "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000206"), companyId, new DateOnly(2026,  8, 31), "Summer Bank Holiday",         "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000207"), companyId, new DateOnly(2026, 12, 25), "Christmas Day",               "GB", now),
                PublicHoliday.Create(Guid.Parse("B0000000-0000-0000-0000-000000000208"), companyId, new DateOnly(2026, 12, 28), "Boxing Day (substitute)",     "GB", now));

            await db.SaveChangesAsync();
        }

        if (!await db.LeavePolicies.AnyAsync())
        {
            var policyId = Guid.Parse("C0000000-0000-0000-0000-000000000001");

            var policy = LeavePolicy.Create(
                policyId,
                companyId,
                "Standard",
                "Default leave policy for all employees",
                carryOverDays: 5,
                allowNegativeBalance: false,
                now);

            db.LeavePolicies.Add(policy);
            await db.SaveChangesAsync();

            // Assign all seeded employees to the standard policy and initialise their balances.
            var leaveTypes = await db.LeaveTypes
                .Where(lt => lt.CompanyId == companyId && lt.IsActive)
                .ToListAsync();

            var policyYear = LeaveYearCalculator.GetPolicyYear(DateTimeOffset.UtcNow, startMonth: 1);
            var effectiveFrom = new DateOnly(policyYear, 1, 1);

            var employeeIds = new[]
            {
                Guid.Parse("30000000-0000-0000-0000-000000000001"),
                Guid.Parse("30000000-0000-0000-0000-000000000002"),
                Guid.Parse("30000000-0000-0000-0000-000000000003"),
                Guid.Parse("30000000-0000-0000-0000-000000000004"),
                Guid.Parse("30000000-0000-0000-0000-000000000005"),
                Guid.Parse("30000000-0000-0000-0000-000000000006"),
                Guid.Parse("30000000-0000-0000-0000-000000000007"),
                Guid.Parse("30000000-0000-0000-0000-000000000008"),
                Guid.Parse("30000000-0000-0000-0000-000000000009"),
                Guid.Parse("30000000-0000-0000-0000-000000000010"),
            };

            foreach (var employeeId in employeeIds)
            {
                db.EmployeeLeavePolicyAssignments.Add(
                    EmployeeLeavePolicyAssignment.Create(
                        Guid.NewGuid(), companyId, employeeId, policyId, effectiveFrom, now));

                db.LeaveBalances.AddRange(leaveTypes.Select(lt => LeaveBalance.Create(
                    Guid.NewGuid(),
                    companyId,
                    employeeId,
                    lt.Id,
                    policyId,
                    policyYear,
                    lt.Behaviour == LeaveTypeBehaviour.Toil ? 0 : lt.DefaultEntitlementDays,
                    now)));
            }

            await db.SaveChangesAsync();
        }

        if (!await db.LeaveRequests.AnyAsync())
        {
            var annualLeaveTypeId = Guid.Parse("A0000000-0000-0000-0000-000000000001");
            var sickLeaveTypeId   = Guid.Parse("A0000000-0000-0000-0000-000000000002");
            var policyId          = Guid.Parse("C0000000-0000-0000-0000-000000000001");
            var policyYear        = LeaveYearCalculator.GetPolicyYear(DateTimeOffset.UtcNow, startMonth: 1);

            var empSarahId  = Guid.Parse("30000000-0000-0000-0000-000000000001"); // Sarah Chen, CTO
            var empJamesId  = Guid.Parse("30000000-0000-0000-0000-000000000002"); // James Okafor, Senior Dev
            var empLauraId  = Guid.Parse("30000000-0000-0000-0000-000000000005"); // Laura Bennett, HR Manager
            var empEmmaId   = Guid.Parse("30000000-0000-0000-0000-000000000009"); // Emma Jones, Account Exec
            var adminId     = Guid.Parse("30000000-0000-0000-0000-000000000005"); // Laura (HR) reviewed

            LeaveRequest Req(Guid id, Guid empId, Guid leaveType, DateOnly start, DateOnly end, decimal days, string? reason = null)
                => LeaveRequest.Create(id, companyId, empId, leaveType, policyId,
                    start, LeaveDayPart.FullDay, end, LeaveDayPart.FullDay, days, reason, now);

            // Sarah Chen — approved 5 days in Jan, pending 4 days in Jul
            var sarahApproved = Req(Guid.Parse("D0000000-0000-0000-0000-000000000001"),
                empSarahId, annualLeaveTypeId, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), 5m, "New Year break");
            sarahApproved.Approve(adminId, now);

            var sarahPending = Req(Guid.Parse("D0000000-0000-0000-0000-000000000002"),
                empSarahId, annualLeaveTypeId, new DateOnly(2026, 7, 14), new DateOnly(2026, 7, 17), 4m, "Summer holiday");

            // James Okafor — approved 2 days in Feb, rejected 1 day in Apr
            var jamesApproved = Req(Guid.Parse("D0000000-0000-0000-0000-000000000003"),
                empJamesId, annualLeaveTypeId, new DateOnly(2026, 2, 16), new DateOnly(2026, 2, 17), 2m);
            jamesApproved.Approve(adminId, now);

            var jamesRejected = Req(Guid.Parse("D0000000-0000-0000-0000-000000000004"),
                empJamesId, annualLeaveTypeId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 1), 1m);
            jamesRejected.Reject(adminId, now, "Sprint release week — cover needed");

            var jamesSick = Req(Guid.Parse("D0000000-0000-0000-0000-000000000005"),
                empJamesId, sickLeaveTypeId, new DateOnly(2026, 5, 11), new DateOnly(2026, 5, 12), 2m);
            jamesSick.Approve(adminId, now);

            // Laura Bennett — approved 2 days in Mar, pending 5 days in Aug
            var lauraApproved = Req(Guid.Parse("D0000000-0000-0000-0000-000000000006"),
                empLauraId, annualLeaveTypeId, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 10), 2m);
            lauraApproved.Approve(adminId, now);

            var lauraPending = Req(Guid.Parse("D0000000-0000-0000-0000-000000000007"),
                empLauraId, annualLeaveTypeId, new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 7), 5m, "Summer holiday");

            // Emma Jones — pending 2 days at end of Jun (upcoming)
            var emmaPending = Req(Guid.Parse("D0000000-0000-0000-0000-000000000008"),
                empEmmaId, annualLeaveTypeId, new DateOnly(2026, 6, 29), new DateOnly(2026, 6, 30), 2m);

            db.LeaveRequests.AddRange(
                sarahApproved, sarahPending,
                jamesApproved, jamesRejected, jamesSick,
                lauraApproved, lauraPending,
                emmaPending);

            // Reflect approved requests in each employee's balance
            var approvedItems = new[]
            {
                (empSarahId,  annualLeaveTypeId, 5m),
                (empJamesId,  annualLeaveTypeId, 2m),
                (empJamesId,  sickLeaveTypeId,   2m),
                (empLauraId,  annualLeaveTypeId, 2m),
            };

            var balances = await db.LeaveBalances
                .Where(b => b.CompanyId == companyId && b.PolicyYear == policyYear &&
                    (b.EmployeeId == empSarahId || b.EmployeeId == empJamesId || b.EmployeeId == empLauraId))
                .ToListAsync();

            foreach (var (empId, typeId, days) in approvedItems)
            {
                var bal = balances.FirstOrDefault(b => b.EmployeeId == empId && b.LeaveTypeId == typeId);
                bal?.RecordUsage(days, now);
            }

            await db.SaveChangesAsync();
        }
    }
}
