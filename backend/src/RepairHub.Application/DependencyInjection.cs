using Microsoft.Extensions.DependencyInjection;
using RepairHub.Application.Auth;
using RepairHub.Application.Users;
using RepairHub.Application.Workshops;

namespace RepairHub.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<AuthService>();
        services.AddScoped<AccessTokenSessionValidator>();
        services.AddScoped<UserService>();
        services.AddScoped<WorkshopService>();
        services.AddScoped<WorkshopProvisioningService>();

        return services;
    }
}
