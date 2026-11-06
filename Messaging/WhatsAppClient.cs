using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth;
using TournamentScheduler.Api.Logging;

namespace TournamentScheduler.Api.Messaging;

// Meta's WhatsApp Cloud API: one POST per message to /{version}/{phone-number-id}/messages. A
// business may only open a conversation with a template Meta has approved, so every message here is
// a template plus its placeholder values. An accepted send says only that Meta took it: a number
// that isn't on WhatsApp is reported later, to a webhook we don't have yet (Messaging/WhatsApp.md).

/// <summary>Meta refused a message; <see cref="Code"/> is its error code (131026: not on WhatsApp, 132001: no such template, …).</summary>
public sealed class WhatsAppException(string message, int? code, int status) : Exception(message)
{
    public int? Code { get; } = code;
    public int Status { get; } = status;
}

/// <summary>
/// Sends one approved template to one number, recording the call in RemoteLogs. The values sent are
/// logged as given in <c>logged</c>, so a code's call is logged with "&lt;code&gt;" in its place.
/// </summary>
public sealed class WhatsAppClient(IHttpClientFactory http, IOptions<WhatsAppOptions> options, RemoteCallRecorder remote)
{
    public const string HttpClientName = "WhatsApp";

    private readonly WhatsAppOptions _options = options.Value;

    /// <summary>
    /// <paramref name="body"/> fills the template's {{1}}, {{2}}, …; <paramref name="button"/> is an
    /// authentication template's copy-code button, which Meta requires to carry the code again.
    /// Returns Meta's message id.
    /// </summary>
    public async Task<string?> SendTemplateAsync(
        string to, string template, IReadOnlyList<string> body, string? button,
        IReadOnlyList<string> logged, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("WhatsApp isn't set up on the server (the WhatsApp section of appsettings needs PhoneNumberId and AccessToken).");

        var number = WhatsAppNumber(to);
        var url = $"{_options.BaseUrl.TrimEnd('/')}/{_options.ApiVersion}/{_options.PhoneNumberId}/messages";
        var call = remote.Start("WHATSAPP", "POST", url, new { to = number, template, parameters = logged });

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(Payload(number, template, body, button))
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

            var client = http.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
            using var response = await client.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = Parse(text);

            if (!response.IsSuccessStatusCode)
            {
                var error = json?["error"];
                var details = error?["error_data"]?["details"]?.GetValue<string>();
                var message = error?["message"]?.GetValue<string>() ?? $"WhatsApp answered {(int)response.StatusCode}.";
                var failure = new WhatsAppException(details is null ? message : $"{message} {details}", error?["code"]?.GetValue<int>(), (int)response.StatusCode);
                call.Failed(failure, failure.Status);
                throw failure;
            }

            var id = json?["messages"]?[0]?["id"]?.GetValue<string>();
            call.Succeeded((int)response.StatusCode, new { messageId = id });
            return id;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            call.Failed(e);
            throw;
        }
    }

    /// <summary>Meta wants the number in international form without the "+": +919876543210 → 919876543210.</summary>
    public static string WhatsAppNumber(string e164) => e164.TrimStart('+');

    private object Payload(string number, string template, IReadOnlyList<string> body, string? button)
    {
        var components = new List<object>();
        if (body.Count > 0)
            components.Add(new { type = "body", parameters = body.Select(v => new { type = "text", text = v }).ToArray() });
        if (button != null)
            components.Add(new { type = "button", sub_type = "url", index = "0", parameters = new[] { new { type = "text", text = button } } });

        return new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = number,
            type = "template",
            template = new { name = template, language = new { code = _options.Language }, components }
        };
    }

    private static JsonNode? Parse(string text)
    {
        try { return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); }
        catch (JsonException) { return null; }
    }
}
