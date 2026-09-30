using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Http;
using TournamentScheduler.Api.Logging;

namespace TournamentScheduler.Api.Gateway;

/// <summary>
/// The secure gateway: <c>POST /api/gateway</c>.
///
/// <list type="number">
/// <item>Reads the envelope and decrypts its body with the server's private key.</item>
/// <item>Checks the request is fresh, not a replay, and that its clear header matches the encrypted one.</item>
/// <item>Turns the service id into the action's real method and URL, puts the decrypted body in
/// place of the encrypted one, and hands the request to the normal pipeline — routing, model
/// binding, validation, the service and the <c>{ status, data }</c> envelope all run exactly as they
/// would for a direct call, so no controller knows the gateway exists.</item>
/// <item>Captures that envelope and returns it encrypted with the request's one-time key.</item>
/// </list>
///
/// Failures before decryption (a malformed envelope, a key the server doesn't hold, a token that
/// fails its integrity check) can only be answered in the clear, since there is no key to answer
/// with; they carry a status and a message and nothing else. Everything after is encrypted.
/// The outer HTTP status mirrors the inner one, as every other response of this API does.
///
/// Every call — answered or refused — becomes one MBMiddlewareLogs row: the envelope as it arrived,
/// the envelope as it left, and why it was refused if it was.
/// </summary>
public sealed class GatewayMiddleware(
    RequestDelegate next,
    IGatewayKeyStore keys,
    ServiceRegistry services,
    ReplayGuard replays,
    IOptions<GatewayOptions> options,
    IOptions<JsonOptions> json,
    TimeProvider clock,
    IWebHostEnvironment environment,
    LogQueue logs,
    LogRedactor redactor,
    IOptions<LogStoreOptions> logOptions,
    ILogger<GatewayMiddleware> logger)
{
    private readonly GatewayOptions _options = options.Value;
    private readonly JsonSerializerOptions _json = json.Value.JsonSerializerOptions;
    private readonly bool _readBodies = logOptions.Value.LogReadResponseBodies;

    /// <summary>Set on every response by <see cref="RequestCorrelationMiddleware"/>; the app's requestUUID for gateway calls.</summary>
    public const string RequestIdHeader = RequestCorrelationMiddleware.Header;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.Equals(GatewayOptions.Path, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var log = new MBMiddlewareLog
        {
            RequestUUID = RequestLogContext.Of(context)?.RequestUUID ?? Guid.NewGuid(),
            ClientIp = context.Connection.RemoteIpAddress?.ToString(),
            StartDate = clock.GetUtcNow().UtcDateTime
        };
        context.Features.Set(log);
        var timer = Stopwatch.StartNew();

        try
        {
            await Process(context);
        }
        finally
        {
            log.HttpStatusCode = context.Response.StatusCode;
            log.EndDate = clock.GetUtcNow().UtcDateTime;
            log.DurationMs = (int)timer.ElapsedMilliseconds;
            logs.Enqueue(log);
        }
    }

    private async Task Process(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await WritePlain(context, StatusCodes.Status405MethodNotAllowed, "Send encrypted requests to the gateway with POST.");
            return;
        }

        if (keys.Current == null)
        {
            await WritePlain(context, StatusCodes.Status503ServiceUnavailable,
                "The server has no encryption key it can read, so it can't accept requests. On the server, create one with " +
                "'dotnet run -- gateway-keys new', or give the site read access to the key folder (see the API log).");
            return;
        }

        var (envelope, readError, readStatus) = await ReadEnvelope(context);
        if (envelope == null)
        {
            await WritePlain(context, readStatus, readError);
            return;
        }

        var header = envelope.RequestHeader!;

        GatewayCrypto.DecryptedRequest decrypted;
        try
        {
            decrypted = GatewayCrypto.DecryptRequest(envelope.RequestBody!.EncryptedData!, keys);
        }
        catch (GatewayCryptoException e)
        {
            logger.LogWarning("Gateway request {RequestUUID} for {ServiceRequestId} refused: {Reason} ({Detail})",
                header.RequestUUID, header.ServiceRequestId, e.Error, e.Message);

            await WritePlain(context, StatusCodes.Status400BadRequest, e.Error == GatewayCryptoError.UnknownKey
                ? "This app is set up with a server key the server no longer has. Update the app's gateway key."
                : "The request couldn't be decrypted. Check the app is set up with this server's gateway key.");
            return;
        }

        try
        {
            using (logger.BeginScope(new Dictionary<string, object?>
                   {
                       ["RequestUUID"] = header.RequestUUID,
                       ["JourneyId"] = header.JourneyId,
                       ["ServiceRequestId"] = header.ServiceRequestId,
                       ["Channel"] = header.Channel
                   }))
            {
                await Handle(context, header, decrypted);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decrypted.ContentKey);
        }
    }

    // -----------------------------------------------------------------
    // Reading the envelope (all answered in the clear)
    // -----------------------------------------------------------------

    private async Task<(GatewayRequest? Envelope, string Error, int Status)> ReadEnvelope(HttpContext context)
    {
        if (context.Request.ContentLength > _options.MaxRequestBytes)
            return (null, "The request is too large.", StatusCodes.Status413PayloadTooLarge);

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > _options.MaxRequestBytes)
                return (null, "The request is too large.", StatusCodes.Status413PayloadTooLarge);
        }

        var raw = buffer.ToArray();
        context.Features.Get<MBMiddlewareLog>()!.EncryptedRequestJson = redactor.Cap(Encoding.UTF8.GetString(raw));

        GatewayRequest? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<GatewayRequest>(raw, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return (null, "The request isn't a valid gateway request.", StatusCodes.Status400BadRequest);
        }

        var header = envelope?.RequestHeader;
        if (header != null) Record(context, header);
        var missing =
            header == null ? "requestHeader"
            : string.IsNullOrWhiteSpace(header.ServiceRequestId) ? "requestHeader.serviceRequestId"
            : string.IsNullOrWhiteSpace(header.RequestUUID) ? "requestHeader.requestUUID"
            : string.IsNullOrWhiteSpace(header.Timestamp) ? "requestHeader.timestamp"
            : string.IsNullOrWhiteSpace(envelope!.RequestBody?.EncryptedData) ? "requestBody.encryptedData"
            : null;
        if (missing != null)
            return (null, $"The gateway request is missing {missing}.", StatusCodes.Status400BadRequest);

        if (!Guid.TryParse(header!.RequestUUID, out _))
            return (null, "requestHeader.requestUUID must be a UUID.", StatusCodes.Status400BadRequest);

        if (ParseTimestamp(header.Timestamp!) == null)
            return (null, "requestHeader.timestamp must be an ISO 8601 date and time.", StatusCodes.Status400BadRequest);

        return (envelope, "", 0);
    }

    // -----------------------------------------------------------------
    // After decryption (answered encrypted)
    // -----------------------------------------------------------------

    private async Task Handle(HttpContext context, GatewayRequestHeader header, GatewayCrypto.DecryptedRequest decrypted)
    {
        var key = decrypted.ContentKey;

        // The clear header is only trusted where the encrypted one agrees with it, so a captured
        // body can't be replayed under a different service id, request id or time.
        var sealedHeader = decrypted.ProtectedHeader;
        if (GatewayCrypto.Text(sealedHeader, "serviceRequestId") != header.ServiceRequestId
            || GatewayCrypto.Text(sealedHeader, "requestUUID") != header.RequestUUID
            || GatewayCrypto.Text(sealedHeader, "timestamp") != header.Timestamp)
        {
            logger.LogWarning("Gateway request header does not match its encrypted copy.");
            await WriteEncrypted(context, header, key, StatusCodes.Status400BadRequest,
                "The request header doesn't match its encrypted contents.");
            return;
        }

        var sentAt = ParseTimestamp(header.Timestamp!)!.Value;
        var skew = clock.GetUtcNow() - sentAt;
        if (skew.Duration() > TimeSpan.FromSeconds(_options.MaxClockSkewSeconds))
        {
            logger.LogWarning("Gateway request is {SkewSeconds:F0}s away from the server clock.", skew.TotalSeconds);
            await WriteEncrypted(context, header, key, StatusCodes.Status400BadRequest,
                $"This device's clock is more than {_options.MaxClockSkewSeconds / 60} minutes away from the server's. " +
                "Set the date and time to automatic, then try again.");
            return;
        }

        if (!replays.TryAccept(Guid.Parse(header.RequestUUID!)))
        {
            logger.LogWarning("Gateway request id was already used.");
            await WriteEncrypted(context, header, key, StatusCodes.Status409Conflict,
                "This request has already been received, so it wasn't processed again.");
            return;
        }

        var service = services.Find(header.ServiceRequestId!);
        if (service == null)
        {
            await WriteEncrypted(context, header, key, StatusCodes.Status404NotFound,
                $"There is no service '{header.ServiceRequestId}'. The app may be newer than the server.");
            return;
        }

        var (target, payloadError) = ReadPayload(decrypted.Plaintext, service);
        if (target == null)
        {
            await WriteEncrypted(context, header, key, StatusCodes.Status400BadRequest, payloadError!);
            return;
        }

        await Dispatch(context, header, service, target, key);
    }

    private sealed record Target(string Path, QueryString Query, byte[] Body);

    /// <summary>The decrypted <c>{ routeParams, query, body }</c>, turned into the action's URL and body.</summary>
    private static (Target? Target, string? Error) ReadPayload(byte[] plaintext, ServiceDescriptor service)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(plaintext);
        }
        catch (JsonException)
        {
            return (null, "The encrypted request body isn't valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, "The encrypted request body must be a JSON object.");

            var routeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty(GatewayPayload.RouteParams, out var routeParams) && routeParams.ValueKind != JsonValueKind.Null)
            {
                if (routeParams.ValueKind != JsonValueKind.Object)
                    return (null, "routeParams must be an object.");
                foreach (var p in routeParams.EnumerateObject())
                {
                    if (Scalar(p.Value) is not { } value)
                        return (null, $"Route parameter '{p.Name}' must be a number or text.");
                    routeValues[p.Name] = value;
                }
            }

            var (path, missing) = ServiceRegistry.FillRoute(service, routeValues);
            if (path == null)
                return (null, $"{service.Id} needs the route parameter '{missing}'.");

            var query = new List<KeyValuePair<string, string?>>();
            if (root.TryGetProperty(GatewayPayload.Query, out var queryValues) && queryValues.ValueKind != JsonValueKind.Null)
            {
                if (queryValues.ValueKind != JsonValueKind.Object)
                    return (null, "query must be an object.");
                foreach (var p in queryValues.EnumerateObject())
                {
                    var items = p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().ToList() : [p.Value];
                    foreach (var item in items)
                    {
                        if (item.ValueKind == JsonValueKind.Null) continue;
                        if (Scalar(item) is not { } value)
                            return (null, $"Query parameter '{p.Name}' must be a number, text or true/false.");
                        query.Add(new(p.Name, value));
                    }
                }
            }

            var body = root.TryGetProperty(GatewayPayload.Body, out var bodyValue) && bodyValue.ValueKind != JsonValueKind.Null
                ? Encoding.UTF8.GetBytes(bodyValue.GetRawText())
                : [];

            return (new Target(path, QueryString.Create(query), body), null);
        }
    }

    private static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };

    private async Task Dispatch(HttpContext context, GatewayRequestHeader header, ServiceDescriptor service, Target target, byte[] key)
    {
        context.Features.Set(new GatewayRequestFeature(header, service));

        var request = context.Request;
        request.Method = service.HttpMethod;
        request.Path = target.Path;
        request.QueryString = target.Query;
        request.Body = new MemoryStream(target.Body);
        request.ContentLength = target.Body.Length;
        request.ContentType = target.Body.Length > 0 ? "application/json; charset=utf-8" : null;

        var original = context.Response.Body;
        using var captured = new MemoryStream();
        context.Response.Body = captured;
        var timer = Stopwatch.StartNew();
        string? crashMessage = null;

        try
        {
            await next(context);
        }
        catch (Exception e) when (!context.RequestAborted.IsCancellationRequested)
        {
            // Caught here rather than by the exception handler further out, so a crash still
            // answers encrypted. Detail only while developing, as for any other crash.
            logger.LogError(e, "Gateway call to {Action} failed.", service.ActionName);
            crashMessage = environment.IsDevelopment() ? e.Message : null;
            captured.SetLength(0);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = null;
        }
        finally
        {
            context.Response.Body = original;
        }

        var status = context.Response.StatusCode == StatusCodes.Status204NoContent
            ? StatusCodes.Status200OK
            : context.Response.StatusCode;

        // Everything in this pipeline answers with the envelope; an empty or non-JSON body means
        // the answer came from outside it (a bare status, a crash) and gets one of its own.
        var isEnvelope = captured.Length > 0 && context.Response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
        var plaintext = isEnvelope
            ? captured.ToArray()
            : JsonSerializer.SerializeToUtf8Bytes(ApiResponses.Create(status, null, crashMessage), _json);

        // A 201's Location header would name the real URL and the new id in the clear.
        context.Response.Headers.Location = default;

        // Reads keep their encrypted answer out of the log unless read bodies are switched on — the
        // live screens poll, and every copy of the same match state would be stored.
        var logBody = !HttpMethods.IsGet(service.HttpMethod) || _readBodies;
        await WriteEncrypted(context, header, key, status, plaintext, logBody);

        logger.LogInformation("Gateway {ServiceRequestId} -> {Action} answered {StatusCode} in {ElapsedMs} ms.",
            service.Id, service.ActionName, status, timer.ElapsedMilliseconds);
    }

    // -----------------------------------------------------------------
    // Writing answers — and recording them in MBMiddlewareLogs
    // -----------------------------------------------------------------

    /// <summary>What the clear header says, into this call's log row and the request's log context.</summary>
    private static void Record(HttpContext context, GatewayRequestHeader header)
    {
        var log = context.Features.Get<MBMiddlewareLog>()!;
        var request = RequestLogContext.Of(context);

        log.ServiceRequestId = header.ServiceRequestId;
        log.JourneyId = header.JourneyId;
        log.SessionId = header.SessionId;
        log.DeviceId = header.DeviceId;
        log.Channel = header.Channel;
        log.AppVersion = header.AppVersion;
        log.KeyId = header.KeyId;

        if (Guid.TryParse(header.RequestUUID, out var requestId))
        {
            log.RequestUUID = requestId;
            if (request != null) request.RequestUUID = requestId;
        }

        if (request != null)
        {
            request.ViaGateway = true;
            request.ServiceRequestId = header.ServiceRequestId;
            request.JourneyId = header.JourneyId;
            request.SessionId = header.SessionId;
            request.DeviceId = header.DeviceId;
            request.Channel = header.Channel;
        }
    }

    /// <summary>A refusal after decryption: encrypted, and recorded as the reason.</summary>
    private Task WriteEncrypted(HttpContext context, GatewayRequestHeader header, byte[] key, int status, string message)
    {
        context.Features.Get<MBMiddlewareLog>()!.RejectedReason = message;
        return WriteEncrypted(context, header, key, status,
            JsonSerializer.SerializeToUtf8Bytes(ApiResponses.Create(status, null, message), _json), logBody: true);
    }

    private async Task WriteEncrypted(HttpContext context, GatewayRequestHeader header, byte[] key, int status, byte[] envelope, bool logBody)
    {
        var response = new GatewayResponse
        {
            ResponseHeader = new GatewayResponseHeader
            {
                ServiceRequestId = header.ServiceRequestId,
                RequestUUID = header.RequestUUID,
                JourneyId = header.JourneyId,
                Timestamp = FormatTimestamp(clock.GetUtcNow())
            },
            ResponseBody = new GatewayEncryptedBody
            {
                EncryptedData = GatewayCrypto.EncryptResponse(envelope, key, header.RequestUUID)
            }
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, _json);
        if (logBody) context.Features.Get<MBMiddlewareLog>()!.EncryptedResponseJson = redactor.Cap(Encoding.UTF8.GetString(bytes));

        context.Response.StatusCode = status;
        context.Response.ContentLength = null;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
    }

    /// <summary>A refusal before decryption: there is no key to answer with, so it goes in the clear.</summary>
    private async Task WritePlain(HttpContext context, int status, string message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(ApiResponses.Create(status, null, message), _json);
        var log = context.Features.Get<MBMiddlewareLog>()!;
        log.RejectedReason = message;
        log.EncryptedResponseJson = Encoding.UTF8.GetString(bytes); // what was sent, clear as it was

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
    }

    private static DateTimeOffset? ParseTimestamp(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
