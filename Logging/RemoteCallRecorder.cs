using System.Diagnostics;
using System.Text.Json;

namespace TournamentScheduler.Api.Logging;

/// <summary>
/// Writes a RemoteLogs row for a call the API makes to a third party (the email server today; SMS
/// and others later). The caller describes the call — never including secrets such as a one-time
/// code or a password — and says how it ended; the row gets the request's id, masking, timing.
/// <code>
/// var call = recorder.Start("SMTP", "SEND", "smtp.gmail.com:587", new { to, subject });
/// try { …; call.Succeeded(250, new { accepted = true }); }
/// catch (Exception e) { call.Failed(e); throw; }
/// </code>
/// </summary>
public sealed class RemoteCallRecorder(LogQueue queue, LogRedactor redactor, IHttpContextAccessor http, TimeProvider clock)
{
    private LogQueue Queue { get; } = queue;
    private LogRedactor Redactor { get; } = redactor;
    private TimeProvider Clock { get; } = clock;

    public RemoteCall Start(string provider, string method, string url, object? request)
    {
        var context = http.HttpContext;
        var log = new RemoteLog
        {
            RequestUUID = context is null ? null : RequestLogContext.Of(context)?.RequestUUID,
            ProviderName = provider,
            HttpMethod = method,
            Url = url,
            RequestJson = request is null ? null : Redactor.Body(JsonSerializer.Serialize(request)),
            StartDate = Clock.GetUtcNow().UtcDateTime
        };
        return new RemoteCall(log, this);
    }

    public sealed class RemoteCall
    {
        private readonly RemoteLog _log;
        private readonly RemoteCallRecorder _recorder;
        private readonly Stopwatch _timer = Stopwatch.StartNew();
        private bool _done;

        internal RemoteCall(RemoteLog log, RemoteCallRecorder recorder)
        {
            _log = log;
            _recorder = recorder;
        }

        public void Succeeded(int? status, object? response = null)
        {
            _log.HttpStatusCode = status;
            _log.ResponseJson = response is null ? null : _recorder.Redactor.Body(JsonSerializer.Serialize(response));
            Finish();
        }

        public void Failed(Exception error, int? status = null)
        {
            var message = error.GetBaseException().Message;
            _log.HttpStatusCode = status;
            _log.ErrorMessage = message.Length > 2000 ? message[..2000] : message;
            Finish();
        }

        private void Finish()
        {
            if (_done) return;
            _done = true;
            _log.EndDate = _recorder.Clock.GetUtcNow().UtcDateTime;
            _log.DurationMs = (int)_timer.ElapsedMilliseconds;
            _recorder.Queue.Enqueue(_log);
        }
    }
}
