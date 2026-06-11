using HR.Infrastructure.Email;
using HR.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Infrastructure;

public static class InfrastructureModule
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IEmailSender, LoggingEmailSender>();
        services.AddSingleton<IInviteLinkBuilder, ConfiguredInviteLinkBuilder>();
        return services;
    }
}
