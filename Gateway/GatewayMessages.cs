using System.Text.Json.Serialization;

namespace TournamentScheduler.Api.Gateway;

// The secure gateway's wire format. The full description, with examples, is in Gateway/README.md.
//
//   request  { "requestHeader": { ... },  "requestBody":  { "encryptedData": "<JWE>" } }
//   response { "responseHeader": { ... }, "responseBody": { "encryptedData": "<JWE>" } }
//
// The headers travel in the clear so the server can log and route a request before decrypting it,
// and so session and authorisation checks can later be made from them. They are not trusted as
// they arrive: serviceRequestId, requestUUID and timestamp are repeated inside the encrypted
// token's protected header, and the gateway refuses a request whose two copies disagree.

public sealed class GatewayRequest
{
    [JsonPropertyName("requestHeader")]
    public GatewayRequestHeader? RequestHeader { get; set; }

    [JsonPropertyName("requestBody")]
    public GatewayEncryptedBody? RequestBody { get; set; }
}

public sealed class GatewayRequestHeader
{
    /// <summary>Which action to call, e.g. CRICKET_BALL_RECORD. See <see cref="ServiceRequestIdAttribute"/>.</summary>
    [JsonPropertyName("serviceRequestId")]
    public string? ServiceRequestId { get; set; }

    /// <summary>A new UUID for every request (retries included). The key for tracing and replay checks.</summary>
    [JsonPropertyName("requestUUID")]
    public string? RequestUUID { get; set; }

    /// <summary>When the app built the request: ISO 8601 in UTC.</summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>Groups the requests of one user flow, so a support question can be traced end to end.</summary>
    [JsonPropertyName("journeyId")]
    public string? JourneyId { get; set; }

    /// <summary>
    /// The signed-in device's session id, as sign-in returned it. Not secret and not trusted: it only
    /// groups a device's requests in the logs. The secret token travels sealed, in the payload's
    /// <c>session</c>; the logs record the session that token actually belongs to.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    /// <summary>WEB, MOBILE_ANDROID or MOBILE_IOS.</summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; set; }

    /// <summary>A random id the app makes once per install (or browser). Not a hardware id.</summary>
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    /// <summary>Which server key the request was encrypted for.</summary>
    [JsonPropertyName("keyId")]
    public string? KeyId { get; set; }

    /// <summary>Version of this envelope format. "1".</summary>
    [JsonPropertyName("apiVersion")]
    public string? ApiVersion { get; set; }
}

public sealed class GatewayEncryptedBody
{
    /// <summary>A JWE in compact serialisation (RFC 7516).</summary>
    [JsonPropertyName("encryptedData")]
    public string? EncryptedData { get; set; }
}

public sealed class GatewayResponse
{
    [JsonPropertyName("responseHeader")]
    public GatewayResponseHeader ResponseHeader { get; set; } = new();

    [JsonPropertyName("responseBody")]
    public GatewayEncryptedBody ResponseBody { get; set; } = new();
}

public sealed class GatewayResponseHeader
{
    [JsonPropertyName("serviceRequestId")]
    public string? ServiceRequestId { get; set; }

    [JsonPropertyName("requestUUID")]
    public string? RequestUUID { get; set; }

    [JsonPropertyName("journeyId")]
    public string? JourneyId { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;
}

/// <summary>
/// What a request carries once decrypted: the values for the action's route, its query string, its
/// body exactly as the action would receive it on its own URL, and — once signed in — the session
/// token, which is why it is only ever sent sealed.
/// <code>{ "routeParams": { "matchId": 42 }, "query": { "sport": "Cricket" }, "body": { ... }, "session": "k7Qm…" }</code>
/// </summary>
public static class GatewayPayload
{
    public const string RouteParams = "routeParams";
    public const string Query = "query";
    public const string Body = "body";
    public const string Session = "session";
}

/// <summary>
/// The request as the gateway accepted it, attached to the HTTP context. Its presence is what marks
/// a request as having come through the gateway; the session check reads the token from here rather
/// than from anything the action receives.
/// </summary>
public sealed class GatewayRequestFeature(GatewayRequestHeader header, ServiceDescriptor service, string? sessionToken = null)
{
    public GatewayRequestHeader Header { get; } = header;
    public ServiceDescriptor Service { get; } = service;

    /// <summary>The payload's <c>session</c>: null when the app isn't signed in.</summary>
    public string? SessionToken { get; } = sessionToken;
}
