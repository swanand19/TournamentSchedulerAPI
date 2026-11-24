using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TournamentScheduler.Api.Pictures;

/// <summary>Where picture files live. One folder today (HLD P-26); Azure Blob Storage can replace it behind this.</summary>
public interface IPictureStore
{
    /// <summary>Keeps the file under a new random name and answers that name (the picture's id).</summary>
    Task<string> SaveAsync(byte[] bytes, string extension);

    /// <summary>Removes a picture that's been replaced or whose owner is gone. Missing files are fine.</summary>
    void Delete(string? id);

    /// <summary>The file for an id, or null when there's no such picture.</summary>
    string? PathOf(string id);
}

/// <summary>
/// Pictures as files in one private folder, named by 128 random bits so they can't be guessed and never
/// change (a new picture gets a new name — which also lets phones cache them for as long as a link lasts).
/// Nothing in the folder is served except through <see cref="PictureLinks"/>.
/// </summary>
public sealed partial class FilePictureStore(IOptions<PictureOptions> options, ILogger<FilePictureStore> logger) : IPictureStore
{
    [GeneratedRegex("^[0-9a-f]{32}\\.(jpg|png)$")]
    private static partial Regex IdPattern();

    public static bool IsId(string? id) => id != null && IdPattern().IsMatch(id);

    private string Folder => options.Value.Directory;

    public async Task<string> SaveAsync(byte[] bytes, string extension)
    {
        System.IO.Directory.CreateDirectory(Folder);
        var id = $"{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}.{extension}";
        await File.WriteAllBytesAsync(Path.Combine(Folder, id), bytes);
        return id;
    }

    public void Delete(string? id)
    {
        if (!IsId(id)) return;
        try
        {
            File.Delete(Path.Combine(Folder, id!));
        }
        catch (IOException ex)
        {
            // A leftover file costs a few KB; the answer that replaced it has already been saved.
            logger.LogWarning(ex, "Couldn't delete picture {PictureId}.", id);
        }
    }

    public string? PathOf(string id)
    {
        if (!IsId(id)) return null;
        var path = Path.Combine(Folder, id);
        return File.Exists(path) ? path : null;
    }
}
