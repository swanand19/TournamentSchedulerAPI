using Microsoft.AspNetCore.Http.Connections;
using TournamentScheduler.Api.Services.Live;

namespace TournamentScheduler.Api.Realtime;

public static class RealtimeRegistration
{
    /// <summary>Live updates over SignalR: tickets, the open lines, the nudges, the sweep — see <see cref="LiveHub"/>.</summary>
    public static IServiceCollection AddLiveUpdates(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<LiveTickets>();
        services.AddSingleton<LiveConnections>();
        services.AddSingleton<ILiveUpdates, LiveUpdates>();
        services.AddSingleton<LiveSessionSweep>();
        services.AddHostedService(sp => sp.GetRequiredService<LiveSessionSweep>());
        services.AddScoped<ILiveService, LiveService>();
        return services;
    }

    /// <summary>
    /// WebSockets only, opened straight away (no negotiate step): one request, so the single-use
    /// ticket is spent exactly once. Under IIS this needs its "WebSocket Protocol" feature; without
    /// it the line never opens and the apps keep polling.
    /// </summary>
    public static IEndpointRouteBuilder MapLiveHub(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<LiveHub>(LiveHub.Path, options => options.Transports = HttpTransportType.WebSockets);
        return endpoints;
    }
}
