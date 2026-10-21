using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Services.Access;
using TournamentScheduler.Api.Services.Knockouts;
using TournamentScheduler.Api.Services.Notifications;

namespace TournamentScheduler.Api.Auth.TournamentAccess;

/// <summary>
/// Runs every action's <see cref="TournamentAccessAttribute"/>: finds the tournament from the route
/// or body, asks <see cref="ITournamentAccessService"/> whether the caller may, and refuses in the
/// usual envelope if not. After a successful action it lets the service keep the scorer lock
/// current, tells the owners when a scoring action finished the last match (phase 6D), and adds
/// the <c>scoring</c> block to any match it answers with.
///
/// An action filter (not middleware) because it needs the bound body — the schedule approval names
/// its tournament there.
/// </summary>
public sealed class TournamentAccessFilter(ITournamentAccessService access, INotifier notifier, IKnockoutService knockouts, ICurrentUser currentUser) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var rule = context.ActionDescriptor.EndpointMetadata.OfType<TournamentAccessAttribute>().FirstOrDefault();
        if (rule == null || !currentUser.IsSignedIn)
        {
            await next();
            return;
        }

        var id = IdFor(context, rule.From);
        if (id == null)
        {
            context.Result = rule.From == TournamentScope.Body
                ? Refusal(400, "A tournament must be specified.")
                : Refusal(404, "Tournament not found.");
            return;
        }

        var sport = context.RouteData.Values.TryGetValue("sport", out var s) ? s?.ToString() : null;
        var outcome = await access.CheckAsync(rule.From, id.Value, sport, rule.Permission, rule.IgnoresScorerLock);
        if (!outcome.IsAllowed)
        {
            context.Result = Refusal(outcome.StatusCode, outcome.Message!);
            return;
        }

        var executed = await next();
        if (executed.Exception != null && !executed.ExceptionHandled) return;
        if (StatusOf(executed.Result) >= 400) return;

        await access.AfterSuccessAsync(rule.Permission);
        if (rule.Permission == TournamentPermission.Score)
        {
            // A match just ended can settle a group or a knockout side; then the "all played" check.
            await knockouts.AfterScoringAsync();
            await notifier.AfterScoringAsync();
        }

        switch ((executed.Result as ObjectResult)?.Value)
        {
            case Match match:
                match.Scoring = await access.ScoringForContextMatchAsync();
                break;
            case CricketMatchStateDto state:
                state.Scoring = await access.ScoringForContextMatchAsync();
                break;
        }
    }

    private static int? IdFor(ActionExecutingContext context, TournamentScope from)
    {
        if (from == TournamentScope.Body)
            return context.ActionArguments.Values.OfType<ITournamentScopedRequest>().FirstOrDefault()?.TournamentId is > 0 and var bodyId
                ? bodyId
                : null;

        var key = from switch
        {
            TournamentScope.Tournament => "id",
            TournamentScope.Schedule => "scheduleId",
            _ => "matchId"
        };
        return context.RouteData.Values.TryGetValue(key, out var raw)
               && int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static int StatusOf(IActionResult? result) => result switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        _ => 200
    };

    /// <summary>A plain message: the response filter turns it into the { status, data } envelope.</summary>
    private static ObjectResult Refusal(int status, string message) => new(message) { StatusCode = status };
}
