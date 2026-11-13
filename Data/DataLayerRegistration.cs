using Microsoft.EntityFrameworkCore;
using TournamentScheduler.Api.Data.DataServices;
using TournamentScheduler.Api.Data.Repositories;
using TournamentScheduler.Api.Data.StoredProcedures;

namespace TournamentScheduler.Api.Data;

public static class DataLayerRegistration
{
    /// <summary>
    /// The database and everything that reaches it. Repositories and data services are registered
    /// once as open generics, so <c>IDataService&lt;AnyEntity&gt;</c> can be injected without
    /// registering each entity. All are scoped: one context, and so one unit of work, per request.
    /// </summary>
    public static IServiceCollection AddDataLayer(this IServiceCollection services, IConfiguration configuration)
    {
        var database = configuration.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>() ?? new DatabaseOptions();
        services.AddDbContext<TournamentDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"), sql =>
            {
                sql.CommandTimeout(database.CommandTimeoutSeconds);
                if (database.RetryOnFailure)
                    sql.EnableRetryOnFailure(database.MaxRetryCount, TimeSpan.FromSeconds(database.MaxRetryDelaySeconds), errorNumbersToAdd: null);
            }));

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped(typeof(IDataService<>), typeof(DataService<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IStoredProcedureExecutor, StoredProcedureExecutor>();

        return services;
    }
}
