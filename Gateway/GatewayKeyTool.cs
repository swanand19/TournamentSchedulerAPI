namespace TournamentScheduler.Api.Gateway;

/// <summary>
/// <c>dotnet run -- gateway-keys new|show|ensure [--dir &lt;folder&gt;]</c> — managing the server's
/// gateway keys from the command line, without starting the web server.
///
/// <list type="bullet">
/// <item><b>new</b>: makes a new key (it becomes the current one) and prints the settings the apps need.</item>
/// <item><b>show</b>: prints the current key's public half and the app settings again.</item>
/// <item><b>ensure</b>: makes a key only if there is none, then shows it. Setup-IisSite.ps1 uses this.</item>
/// </list>
///
/// Rotating: run <c>new</c>, restart the API, update both apps, and once no phone still uses the
/// old key, delete its .pem file and restart again. Until then the server accepts both.
/// </summary>
public static class GatewayKeyTool
{
    public const string Command = "gateway-keys";

    /// <summary>Runs the command when <paramref name="args"/> ask for it; false means start the API as usual.</summary>
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || args[0] != Command) return false;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var options = new GatewayOptions();
        configuration.GetSection(GatewayOptions.Section).Bind(options);

        var dirIndex = Array.IndexOf(args, "--dir");
        var directory = dirIndex >= 0 && dirIndex + 1 < args.Length ? args[dirIndex + 1] : options.KeyDirectory;
        var verb = args.Length > 1 ? args[1] : "show";

        var keys = GatewayKeyFiles.Load(directory);
        GatewayKey? current;
        switch (verb)
        {
            case "new":
                current = GatewayKeyFiles.Create(directory);
                Console.WriteLine($"Created gateway key {current.Id}.");
                if (options.CurrentKeyId != null)
                    Console.WriteLine($"Note: appsettings pins Gateway:CurrentKeyId to {options.CurrentKeyId}; change it to {current.Id} to make this key current.");
                break;
            case "ensure":
                current = GatewayKeyFiles.PickCurrent(keys, options.CurrentKeyId);
                if (current == null)
                {
                    current = GatewayKeyFiles.Create(directory);
                    Console.WriteLine($"Created gateway key {current.Id}.");
                }
                break;
            case "show":
                current = GatewayKeyFiles.PickCurrent(keys, options.CurrentKeyId);
                if (current == null)
                {
                    Console.Error.WriteLine($"There is no gateway key in {directory}. Create one with: dotnet run -- {Command} new");
                    exitCode = 1;
                    return true;
                }
                break;
            default:
                Console.Error.WriteLine($"Usage: dotnet run -- {Command} new|show|ensure [--dir <folder>]");
                exitCode = 2;
                return true;
        }

        Print(current, directory);
        foreach (var key in keys.Values) key.Dispose();
        current.Dispose();
        return true;
    }

    private static void Print(GatewayKey key, string directory)
    {
        Console.WriteLine($"""

            Gateway key {key.Id} (P-256), kept in {directory}
            Public key: {key.PublicKeyBase64Url}

            Put these in the apps, then restart their dev servers:

              tournament-scheduler-mobile/.env.local
                EXPO_PUBLIC_GATEWAY_KEY_ID={key.Id}
                EXPO_PUBLIC_GATEWAY_PUBLIC_KEY={key.PublicKeyBase64Url}

              tournament-scheduler-ui/.env
                VITE_GATEWAY_KEY_ID={key.Id}
                VITE_GATEWAY_PUBLIC_KEY={key.PublicKeyBase64Url}

            Restart the API (or run deploy\Publish-IisSite.ps1) so it loads the key.
            The public key is safe to share; the .pem file is not.
            """);
    }
}
