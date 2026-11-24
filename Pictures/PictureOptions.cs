namespace TournamentScheduler.Api.Pictures;

/// <summary>
/// Where pictures (profile photos, tournament logos and banners) are kept and how long their links last
/// (HLD P-26). Section "Pictures".
/// </summary>
public class PictureOptions
{
    public const string Section = "Pictures";

    /// <summary>
    /// The folder pictures are written to. Default: <c>pictures</c> under <see cref="Hosting.AppPaths"/> —
    /// <c>C:\ProgramData\TournamentScheduler\pictures</c> on the laptop (the IIS pool needs modify on it), and
    /// <c>$HOME/data/TournamentScheduler/pictures</c> on Azure, App Service's persistent disk.
    /// </summary>
    public string Directory { get; set; } = Hosting.AppPaths.Under("pictures");

    /// <summary>
    /// The key that signs picture links (base64, 32 bytes). Unset: one is made the first time and kept in the
    /// pictures folder, so links survive restarts. Set it (Key Vault) to keep links valid across servers.
    /// </summary>
    public string? LinkKey { get; set; }

    /// <summary>
    /// A picture this big or bigger is refused. The apps send about 50–250 KB after shrinking; sealed for the
    /// gateway a picture grows about 1.8 times, and the gateway takes at most 1 MiB a request.
    /// </summary>
    public int MaxBytes { get; set; } = 500_000;
}
