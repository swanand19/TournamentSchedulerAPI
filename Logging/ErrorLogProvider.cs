namespace TournamentScheduler.Api.Logging;

/// <summary>
/// Plugs ErrorLogs into .NET's own logging: anything logged at Error or Critical — by our code, by
/// ASP.NET's exception handler, by Entity Framework when SQL Server doesn't answer, anywhere —
/// becomes an ErrorLogs row, with the request it happened in and that request's plain body. Nobody
/// has to remember to log an error to the database; logging it at all is enough.
///
/// The Logging folder's own components are left out, so a failure to write logs can never feed
/// itself (they only ever log warnings anyway).
/// </summary>
public sealed class ErrorLogProvider(LogQueue queue, IHttpContextAccessor http, TimeProvider clock) : ILoggerProvider
{
    private static readonly string OwnNamespace = typeof(ErrorLogProvider).Namespace!;

    private LogQueue Queue { get; } = queue;
    private IHttpContextAccessor Http { get; } = http;
    private TimeProvider Clock { get; } = clock;

    public ILogger CreateLogger(string categoryName) => new ErrorLogger(categoryName, this);

    public void Dispose() { }

    private sealed class ErrorLogger(string category, ErrorLogProvider provider) : ILogger
    {
        private readonly bool _ignored = category.StartsWith(OwnNamespace, StringComparison.Ordinal);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Error && logLevel != LogLevel.None && !_ignored && provider.Queue.Enabled;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var context = provider.Http.HttpContext;
            var request = context is null ? null : RequestLogContext.Of(context);
            var said = formatter(state, exception);

            provider.Queue.Enqueue(new ErrorLog
            {
                Crd = provider.Clock.GetUtcNow().UtcDateTime,
                RequestUUID = request?.RequestUUID,
                ServiceRequestId = request?.ServiceRequestId,
                Url = request?.Url ?? (context is null ? null : context.Request.Path + context.Request.QueryString),
                RequestJson = request?.RequestJson,
                ExceptionType = exception?.GetType().FullName,
                Message = exception?.Message ?? said,
                LogMessage = said is { Length: > 2000 } ? said[..2000] : said,
                StackTrace = exception?.StackTrace,
                InnerException = exception?.InnerException?.ToString(),
                Source = category,
                Severity = logLevel.ToString()
            });
        }
    }
}
