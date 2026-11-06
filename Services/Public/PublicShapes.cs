using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TournamentScheduler.Api.Services.Public;

/// <summary>
/// Turns an answer the signed-in apps get into what anyone with a public link may see: the same
/// shape, with everything about people and permissions taken out — contact details, account ids,
/// join codes, the caller's access, the scorer lock and the scoring buttons. A rule applied to the
/// whole answer, at every depth, so a field added to a model later can't slip out unnoticed by name
/// alone: the tests also search every public answer for an email or a number.
/// </summary>
public static class PublicShapes
{
    /// <summary>Property names (camelCase, as sent) never shown publicly.</summary>
    public static readonly IReadOnlySet<string> Hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "email", "emailId", "phone", "phoneNumber", "userId", "isMe", "isLinked",
        "joinCode", "visibleJoinCode", "publicCode", "share",
        "access", "scoring", "actions", "canOpen", "canEdit",
        "activeScorer", "activeScorerUserId", "scorerLastSeenAt", "scoringRequestedByUserId", "scoringRequestedAt",
        "createdByUserId", "completedByUserId", "crd", "crdBy", "lmd", "lmdBy", "myPlayerId", "myTeamId", "myRoles"
    };

    // As the API writes JSON (Program.cs): camelCase, enums as words, cycles skipped.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Converters = { new JsonStringEnumConverter() }
    };

    public static JsonNode? Of(object? value)
    {
        var node = JsonSerializer.SerializeToNode(value, Json);
        Scrub(node);
        return node;
    }

    private static void Scrub(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).Where(Hidden.Contains).ToList()) obj.Remove(key);
                foreach (var (_, child) in obj) Scrub(child);
                break;
            case JsonArray array:
                foreach (var child in array) Scrub(child);
                break;
        }
    }
}
