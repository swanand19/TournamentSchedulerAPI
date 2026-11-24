using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace TournamentScheduler.Api.Pictures;

/// <summary>
/// Read-only links to one picture that stop working after a day or two (HLD P-26). The apps get them inside
/// the usual sealed answers, and only for pictures the caller may see; an image component then loads the
/// picture straight from <c>/api/pictures/{id}</c> — the one way a picture leaves the server.
///
/// A link is relative to the API's base address (<c>pictures/{id}?e=…&amp;s=…</c>): the server can't tell
/// which address a phone reached it by, and the apps already know theirs. The expiry is rounded to the day,
/// so a picture keeps the same link all day and phones cache it instead of loading it again.
/// </summary>
public sealed class PictureLinks(IOptions<PictureOptions> options, TimeProvider clock, ILogger<PictureLinks> logger)
{
    private readonly Lazy<byte[]> _key = new(() => LoadKey(options.Value, logger));

    /// <summary>The link for a picture id, or null when there's no picture.</summary>
    public string? For(string? id)
    {
        if (!FilePictureStore.IsId(id)) return null;
        // Valid until the end of tomorrow (UTC): between 24 and 48 hours, the same link all day.
        var expires = new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date.AddDays(2), TimeSpan.Zero).ToUnixTimeSeconds();
        return $"pictures/{id}?e={expires}&s={Sign(id!, expires)}";
    }

    /// <summary>Is this the link we made for that picture, and still in date? Answers how long it has left.</summary>
    public bool Check(string id, string? expires, string? signature, out TimeSpan left)
    {
        left = TimeSpan.Zero;
        if (!long.TryParse(expires, out var e) || string.IsNullOrEmpty(signature)) return false;
        left = DateTimeOffset.FromUnixTimeSeconds(e) - clock.GetUtcNow();
        if (left <= TimeSpan.Zero) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(id, e)), Encoding.ASCII.GetBytes(signature));
    }

    private string Sign(string id, long expires)
    {
        var mac = HMACSHA256.HashData(_key.Value, Encoding.UTF8.GetBytes($"{id}|{expires}"));
        return Convert.ToBase64String(mac, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>The setting's key, else the one kept beside the pictures (made once), else a key for this run only.</summary>
    private static byte[] LoadKey(PictureOptions options, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(options.LinkKey)) return Convert.FromBase64String(options.LinkKey);

        var file = Path.Combine(options.Directory, ".link-key");
        try
        {
            if (File.Exists(file)) return Convert.FromBase64String(File.ReadAllText(file).Trim());
            Directory.CreateDirectory(options.Directory);
            var key = RandomNumberGenerator.GetBytes(32);
            File.WriteAllText(file, Convert.ToBase64String(key));
            return key;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            // Pictures still show; their links just change after a restart.
            logger.LogWarning(ex, "Couldn't keep the picture link key in {Folder}; links last until the API restarts.", options.Directory);
            return RandomNumberGenerator.GetBytes(32);
        }
    }
}

public static class PictureRegistration
{
    public const string PathPrefix = "/api/pictures";

    public static IServiceCollection AddPictures(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PictureOptions>(configuration.GetSection(PictureOptions.Section));
        services.AddSingleton<IPictureStore, FilePictureStore>();
        services.AddSingleton<PictureLinks>();
        return services;
    }

    /// <summary>
    /// Serves <c>GET /api/pictures/{id}?e=…&amp;s=…</c> before logging, the gateway and sign-in: a picture is
    /// loaded by an image component, which can't open a sealed answer or send a session (HLD P-26). The
    /// signed, expiring link is the permission; anything else is a plain 404, so ids can't be probed.
    /// </summary>
    public static WebApplication UsePictures(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "";
            if (!path.StartsWith(PathPrefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            var id = path[(PathPrefix.Length + 1)..];
            var links = context.RequestServices.GetRequiredService<PictureLinks>();
            var store = context.RequestServices.GetRequiredService<IPictureStore>();
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";

            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
                || !links.Check(id, context.Request.Query["e"], context.Request.Query["s"], out var left)
                || store.PathOf(id) is not { } file)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = id.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg";
            // The file behind a name never changes, so it may be kept for as long as the link lasts — on this device only.
            headers.CacheControl = $"private, max-age={(long)left.TotalSeconds}, immutable";
            await context.Response.SendFileAsync(file);
        });
        return app;
    }
}
