namespace TournamentScheduler.Api.Gateway;

public static class GatewayRegistration
{
    public static IServiceCollection AddGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GatewayOptions>(configuration.GetSection(GatewayOptions.Section));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGatewayKeyStore, FileGatewayKeyStore>();
        services.AddSingleton<ServiceRegistry>();
        services.AddSingleton<ReplayGuard>();
        return services;
    }

    /// <summary>Before routing: the gateway decides the URL the router then sees.</summary>
    public static IApplicationBuilder UseGateway(this IApplicationBuilder app) =>
        app.UseMiddleware<GatewayMiddleware>();

    /// <summary>After routing: needs to know which action a direct call is for.</summary>
    public static IApplicationBuilder UseGatewayEnforcement(this IApplicationBuilder app) =>
        app.UseMiddleware<GatewayEnforcementMiddleware>();
}
