using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Gateway;

namespace TournamentScheduler.Api.Logging;

/// <summary>
/// Writes an MBActivityLogs row for every call that reaches an action, whether it came through the
/// gateway (already decrypted and rewritten by then) or straight to the action's URL: the plain
/// request body, the { status, data } envelope it answered with, and who, what and how long.
///
/// Runs after routing, so it knows the controller and action. Requests that match no action (an
/// unknown URL) aren't activity; refusals by the gateway itself are in MBMiddlewareLogs.
///
/// Reads skip their response body unless <c>LogReadResponseBodies</c> is on — their status, message
/// and size are still recorded.
/// </summary>
public sealed class ActivityLogMiddleware(
    RequestDelegate next,
    LogQueue queue,
    LogRedactor redactor,
    IOptions<LogStoreOptions> options,
    TimeProvider clock)
{
    private readonly bool _readBodies = options.Value.LogReadResponseBodies;

    public async Task InvokeAsync(HttpContext context)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (!queue.Enabled || action == null)
        {
            await next(context);
            return;
        }

        var request = context.Request;
        var log = RequestLogContext.Of(context) ?? new RequestLogContext { RequestUUID = Guid.NewGuid() };
        var serviceId = context.GetEndpoint()!.Metadata.GetMetadata<ServiceRequestIdAttribute>()?.Id;
        log.ServiceRequestId ??= serviceId;
        log.Url = request.Path + request.QueryString;
        log.RequestJson = redactor.Body(await ReadBody(request));

        var started = clock.GetUtcNow();
        var timer = Stopwatch.StartNew();
        var original = context.Response.Body;
        using var captured = new MemoryStream();
        context.Response.Body = captured;
        var crashed = false;

        try
        {
            await next(context);
        }
        catch
        {
            crashed = true; // the exception handler further out answers; the row still gets written
            throw;
        }
        finally
        {
            context.Response.Body = original;
            if (!crashed && captured.Length > 0)
            {
                captured.Position = 0;
                await captured.CopyToAsync(original, context.RequestAborted);
            }

            var status = crashed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
            var body = crashed ? [] : captured.ToArray();
            var (isSuccess, message) = ReadStatus(body);
            var isRead = HttpMethods.IsGet(request.Method);

            queue.Enqueue(new MBActivityLog
            {
                RequestUUID = log.RequestUUID,
                SessionId = log.SessionId,
                JourneyId = log.JourneyId,
                DeviceId = log.DeviceId,
                Channel = log.Channel,
                ServiceRequestId = serviceId,
                ControllerName = action.ControllerName,
                ActionName = action.ActionName,
                HttpMethod = request.Method,
                Url = log.Url,
                RequestJson = log.RequestJson,
                ResponseJson = isRead && !_readBodies ? null : redactor.Body(body),
                HttpStatusCode = status,
                IsSuccess = crashed ? false : isSuccess,
                StatusMessage = crashed ? "The action threw; see ErrorLogs." : message,
                ViaGateway = log.ViaGateway,
                StartDate = started.UtcDateTime,
                EndDate = clock.GetUtcNow().UtcDateTime,
                DurationMs = (int)timer.ElapsedMilliseconds
            });
        }
    }

    private static async Task<string?> ReadBody(HttpRequest request)
    {
        if (request.ContentLength == 0 || (request.ContentLength == null && !request.Headers.TransferEncoding.Any()))
            return null;

        // The action reads the body after this, so it has to be readable twice.
        if (!request.Body.CanSeek) request.EnableBuffering();
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var text = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
        request.Body.Position = 0;
        return text;
    }

    /// <summary>status.isSuccess and status.message from the envelope, when the body is one.</summary>
    private static (bool? IsSuccess, string? Message) ReadStatus(byte[] body)
    {
        if (body.Length == 0) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object)
                return (null, null);

            bool? ok = status.TryGetProperty("isSuccess", out var s) && s.ValueKind is JsonValueKind.True or JsonValueKind.False ? s.GetBoolean() : null;
            var message = status.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            return (ok, message is { Length: > 1000 } ? message[..1000] : message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
