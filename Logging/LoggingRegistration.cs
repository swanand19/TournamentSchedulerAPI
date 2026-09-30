using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace TournamentScheduler.Api.Logging;

public static class LoggingRegistration
{
    /// <summary>The four log tables: queue, writer, masking, the ErrorLogs hook, and the logs database.</summary>
    public static IServiceCollection AddLogStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LogStoreOptions>(configuration.GetSection(LogStoreOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();

        services.AddSingleton<LogQueue>();
        services.AddSingleton<LogRedactor>();
        services.AddSingleton<LogFallbackFile>();

        var connection = configuration.GetConnectionString(LogStoreOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connection))
        {
            services.AddSingleton<ILogStore, UnconfiguredLogStore>();
        }
        else
        {
            // No logger on this context: if a log write fails, EF logging that failure as an error
            // would queue an ErrorLogs row, whose write would fail, and so on.
            services.AddDbContextFactory<LogDbContext>(o => o
                .UseSqlServer(connection)
                .UseLoggerFactory(NullLoggerFactory.Instance));
            services.AddSingleton<ILogStore, SqlLogStore>();
        }

        services.AddHostedService<LogWriterService>();
        services.AddSingleton<ILoggerProvider, ErrorLogProvider>();
        return services;
    }

    /// <summary>First in the pipeline: every request gets its id before anything can fail.</summary>
    public static IApplicationBuilder UseRequestCorrelation(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestCorrelationMiddleware>();

    /// <summary>After routing: needs to know the controller and action.</summary>
    public static IApplicationBuilder UseActivityLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<ActivityLogMiddleware>();
}

/// <summary>
/// Lets <c>dotnet ef</c> build the logs context without starting the API. The connection string is
/// only used by <c>database update</c>; <c>migrations add</c> and <c>migrations script</c> never connect.
/// </summary>
public sealed class LogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<LogDbContext>
{
    public LogDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connection = configuration.GetConnectionString(LogStoreOptions.ConnectionStringName)
                         ?? "Server=(localdb)\\MSSQLLocalDB;Database=TournamentSchedulerLogs;Trusted_Connection=True";
        return new LogDbContext(new DbContextOptionsBuilder<LogDbContext>().UseSqlServer(connection).Options);
    }
}
