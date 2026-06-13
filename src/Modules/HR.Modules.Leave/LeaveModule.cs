using FluentValidation;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.ApproveLeaveRequest;
using HR.Modules.Leave.Features.AssignLeavePolicyToEmployee;
using HR.Modules.Leave.Features.CancelLeaveRequest;
using HR.Modules.Leave.Features.CreatePublicHoliday;
using HR.Modules.Leave.Features.ListPublicHolidays;
using HR.Modules.Leave.Features.RejectLeaveRequest;
using HR.Modules.Leave.Features.CreateLeavePolicy;
using HR.Modules.Leave.Features.GetEmployeeLeaveBalance;
using HR.Modules.Leave.Features.GetLeavePolicy;
using HR.Modules.Leave.Features.SubmitLeaveRequest;
using HR.Modules.Leave.Features.InitialiseEmployeeLeave;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
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
        services.AddScoped<GetLeavePolicyHandler>();
        services.AddScoped<AssignLeavePolicyToEmployeeHandler>();
        services.AddScoped<IValidator<AssignLeavePolicyToEmployeeRequest>, AssignLeavePolicyToEmployeeValidator>();
        services.AddScoped<GetEmployeeLeaveBalanceHandler>();
        services.AddScoped<IValidator<GetEmployeeLeaveBalanceRequest>, GetEmployeeLeaveBalanceValidator>();
        services.AddScoped<SubmitLeaveRequestHandler>();
        services.AddScoped<IValidator<SubmitLeaveRequestRequest>, SubmitLeaveRequestValidator>();
        services.AddScoped<CancelLeaveRequestHandler>();
        services.AddScoped<IValidator<CancelLeaveRequestRequest>, CancelLeaveRequestValidator>();
        services.AddScoped<ApproveLeaveRequestHandler>();
        services.AddScoped<IValidator<ApproveLeaveRequestRequest>, ApproveLeaveRequestValidator>();
        services.AddScoped<RejectLeaveRequestHandler>();
        services.AddScoped<IValidator<RejectLeaveRequestRequest>, RejectLeaveRequestValidator>();
        services.AddScoped<IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>, EmployeeCreatedHandler>();
        services.AddScoped<CreatePublicHolidayHandler>();
        services.AddScoped<IValidator<CreatePublicHolidayRequest>, CreatePublicHolidayValidator>();
        services.AddScoped<ListPublicHolidaysHandler>();
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

        if (await db.LeaveTypes.AnyAsync())
            return;

        var now = DateTimeOffset.UtcNow;
        var companyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        db.LeaveTypes.AddRange(
            LeaveType.Create(Guid.Parse("A0000000-0000-0000-0000-000000000001"), companyId, "Annual Leave",        "ANNUAL",        25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now),
            LeaveType.Create(Guid.Parse("A0000000-0000-0000-0000-000000000002"), companyId, "Sick Leave",          "SICK",          10, AccrualMethod.None,    LeaveTypeBehaviour.Sickness,  now),
            LeaveType.Create(Guid.Parse("A0000000-0000-0000-0000-000000000003"), companyId, "Unpaid Leave",        "UNPAID",         0, AccrualMethod.None,    LeaveTypeBehaviour.Unpaid,    now),
            LeaveType.Create(Guid.Parse("A0000000-0000-0000-0000-000000000004"), companyId, "Compassionate Leave", "COMPASSIONATE",  5, AccrualMethod.None,    LeaveTypeBehaviour.Standard,  now),
            LeaveType.Create(Guid.Parse("A0000000-0000-0000-0000-000000000005"), companyId, "Parental Leave",      "PARENTAL",      52, AccrualMethod.None,    LeaveTypeBehaviour.Parental,  now),
            LeaveType.Create(Guid.Parse("A0000000-0000-0000-0000-000000000006"), companyId, "Time Off In Lieu",    "TOIL",           0, AccrualMethod.None,    LeaveTypeBehaviour.Standard,  now));

        await db.SaveChangesAsync();
    }
}
