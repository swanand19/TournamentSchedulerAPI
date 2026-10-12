using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Services.Accounts;

namespace TournamentScheduler.Api.Auth;

/// <summary>
/// <c>dotnet run -- users create-admin &lt;email&gt; [name]</c> — makes the first admin without starting
/// the web server (on the IIS server: <c>dotnet TournamentScheduler.Api.dll users create-admin …</c>
/// from the site folder, so it uses that folder's connection string).
///
/// Promotes the account if the email already has one; otherwise creates it (then the name is
/// needed). The admin signs in like anyone else, with a code sent to that email; in the admin
/// portal they also choose a password at their first sign-in.
///
/// <c>users reset-password &lt;email&gt;</c> clears a forgotten portal password, for when there is no
/// other admin to do it from the portal.
/// </summary>
public static class UserAdminTool
{
    public const string Command = "users";

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || args[0] != Command) return false;

        if (args.Length < 3 || args[1] is not ("create-admin" or "reset-password"))
        {
            Console.Error.WriteLine($"Usage: dotnet run -- {Command} create-admin <email> [name]");
            Console.Error.WriteLine($"       dotnet run -- {Command} reset-password <email>   (clears a forgotten admin-portal password)");
            exitCode = 2;
            return true;
        }

        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                          ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                          ?? "Production";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddDataLayer(configuration)
            .AddScoped<IUserAdminService, UserAdminService>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var name = args.Length > 3 ? string.Join(' ', args.Skip(3)) : null;

        try
        {
            var admin = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
            var result = (args[1] == "reset-password" ? admin.ResetPasswordAsync(args[2]) : admin.MakeAdminAsync(args[2], name)).GetAwaiter().GetResult();
            if (result.IsOk)
            {
                Console.WriteLine(result.Value);
            }
            else
            {
                Console.Error.WriteLine(result.Error);
                exitCode = 1;
            }
        }
        catch (Exception e) when (e is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Couldn't reach the database ({e.GetBaseException().Message}). Check DefaultConnection, and that the accounts migration has been applied.");
            exitCode = 1;
        }

        return true;
    }
}
