using Microsoft.AspNetCore.HttpOverrides;
using TournamentScheduler.Api.Gateway;
using TournamentScheduler.Api.Http;

namespace TournamentScheduler.Api.Hosting;

/// <summary>
/// The <c>Hosting</c> section: what changes between the laptop (IIS on the home network, plain HTTP for
/// phones) and Azure App Service (HTTPS at Azure's front end, which forwards to the API).
/// </summary>
public sealed class HostingOptions
{
    public const string Section = "Hosting";

    /// <summary>
    /// Send plain-HTTP requests to HTTPS. Off on the laptop's IIS site (phones there can't trust the laptop's
    /// certificate) and on Azure (App Service's "HTTPS only" redirects before the request reaches us).
    /// </summary>
    public bool RedirectToHttps { get; set; } = true;

    /// <summary>
    /// Trust the <c>X-Forwarded-For</c> / <c>-Proto</c> headers Azure's front end adds. Without this every
    /// request seems to come from the front end's own address, so the per-address limits (sign-in codes, the
    /// public pages) would be shared by everyone at once. Only the last hop is read, which is the one Azure
    /// wrote — a client can't fake its address by sending the header itself. Never on where nothing in front
    /// rewrites the header (IIS on the laptop sets the address itself).
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }

    /// <summary>Tell browsers to use HTTPS only for this address (a year). Only where HTTPS is real.</summary>
    public bool Hsts { get; set; }

    /// <summary>
    /// Web pages allowed to call the API from a browser (the web app and the admin portal, e.g.
    /// <c>https://….azurestaticapps.net</c>). Empty means any — the laptop, where the web app's address
    /// changes with the Wi-Fi. Phones aren't browsers and ignore this.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}

public static class HostingRegistration
{
    public const string CorsPolicy = "AllowFrontend";

    /// <summary>Where App Service's health check looks: "is the process up?" — no database, no logs, no gateway.</summary>
    public const string LivePath = "/api/health/live";

    public static IServiceCollection AddHosting(this IServiceCollection services, IConfiguration configuration)
    {
        var hosting = configuration.GetSection(HostingOptions.Section).Get<HostingOptions>() ?? new HostingOptions();
        services.Configure<HostingOptions>(configuration.GetSection(HostingOptions.Section));

        services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
        {
            var origins = hosting.AllowedOrigins.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim().TrimEnd('/')).ToArray();
            if (origins.Length == 0) policy.AllowAnyOrigin();
            else policy.WithOrigins(origins);
            policy.AllowAnyMethod().AllowAnyHeader().WithExposedHeaders(GatewayMiddleware.RequestIdHeader);
        }));

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // App Service's front-end addresses aren't fixed, so no list of known proxies; ForwardLimit (1)
            // keeps it to the one hop Azure added.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = false; // *.azurewebsites.net isn't ours to speak for
        });

        return services;
    }

    /// <summary>
    /// First in the pipeline: the caller's real address (everything after it reads it), and the health probe,
    /// answered before logging so a check every minute doesn't fill MBActivityLogs.
    /// </summary>
    public static WebApplication UseHostingEdge(this WebApplication app)
    {
        var hosting = app.Configuration.GetSection(HostingOptions.Section).Get<HostingOptions>() ?? new HostingOptions();
        if (hosting.TrustForwardedHeaders) app.UseForwardedHeaders();

        app.Map(LivePath, live => live.Run(context =>
            context.Response.WriteAsJsonAsync(ApiResponses.Create(StatusCodes.Status200OK, new { status = "ok" }, null))));
        return app;
    }

    /// <summary>CORS (before the gateway, so its answers and the browser's preflight carry the headers), HSTS, HTTPS.</summary>
    public static WebApplication UseHostingSecurity(this WebApplication app)
    {
        var hosting = app.Configuration.GetSection(HostingOptions.Section).Get<HostingOptions>() ?? new HostingOptions();

        app.Use((context, next) =>
        {
            // JSON only, never framed or sniffed, and no address leaking to other sites.
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            return next(context);
        });

        app.UseCors(CorsPolicy);
        if (hosting.Hsts) app.UseHsts();
        if (hosting.RedirectToHttps) app.UseHttpsRedirection();
        return app;
    }
}
