namespace TournamentScheduler.Api.Hosting;

/// <summary>
/// Where the API keeps files of its own (gateway keys, the log fallback, the development code file) when a
/// setting doesn't say otherwise. On Windows that's <c>C:\ProgramData\TournamentScheduler</c>, shared by
/// <c>dotnet run</c> and IIS. Elsewhere it's <c>$HOME/data/TournamentScheduler</c>: on Azure App Service for
/// Linux, <c>/home</c> is the one disk that is writable and survives restarts — the Linux meaning of
/// "common application data" (<c>/usr/share</c>) is read-only there.
/// </summary>
public static class AppPaths
{
    public static string DataRoot { get; } = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TournamentScheduler")
        : Path.Combine(HomeDirectory(), "data", "TournamentScheduler");

    public static string Under(params string[] parts) => Path.Combine([DataRoot, .. parts]);

    private static string HomeDirectory() =>
        Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home
            ? home
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
