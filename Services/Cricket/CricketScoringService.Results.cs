using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services.Cricket;

/// <summary>
/// When an innings is over, and what the match then says.
///
/// Result wording is cricket's, not a scoreline: a side defending a total wins by runs, a side
/// chasing one wins by wickets with balls to spare, and a side that never had to bat again wins by
/// an innings. Getting that wrong is the difference between a scorecard and a spreadsheet.
/// </summary>
public partial class CricketScoringService
{
    // -----------------------------------------------------------------
    // Ending an innings
    // -----------------------------------------------------------------

    public async Task<CricketResult<CricketMatch>> EndInningsAsync(int matchId, EndInningsRequest request)
    {
        var match = await LoadAsync(matchId);
        if (match == null) return CricketResult<CricketMatch>.Missing("Match not found.");

        var innings = CurrentInnings(match);
        if (innings == null || innings.Status != InningsStatus.InProgress)
            return CricketResult<CricketMatch>.Fail("No innings is under way.");

        if (request.Reason == InningsEndReason.Declared && !match.Rules.IsMultiInnings)
            return CricketResult<CricketMatch>.Fail("An innings can only be declared in a multi-innings match.");

        if (request.Reason is not (InningsEndReason.Declared or InningsEndReason.Abandoned))
            return CricketResult<CricketMatch>.Fail("Only a declaration or an abandonment is the scorer's to call.");

        CloseInnings(match, innings, request.Reason);

        await _unitOfWork.SaveChangesAsync();
        return CricketResult<CricketMatch>.Success(match);
    }

    /// <summary>The three ways an innings ends by itself, in the order the laws settle them.</summary>
    private void CloseInningsIfFinished(CricketMatch match, CricketInnings innings)
    {
        if (innings.Status != InningsStatus.InProgress) return;

        InningsEndReason? reason =
            // Reaching the target ends it on the spot, whatever else happened on that ball.
            innings.Target is int target && innings.Runs >= target ? InningsEndReason.TargetChased
            : innings.Wickets >= innings.WicketsToEndInnings ? InningsEndReason.AllOut
            : innings.BallsAllowed is int cap && innings.LegalBalls >= cap ? InningsEndReason.OversComplete
            : null;

        if (reason is null) return;

        CloseInnings(match, innings, reason.Value);
    }

    private void CloseInnings(CricketMatch match, CricketInnings innings, InningsEndReason reason)
    {
        // Play stopping for good is, to DLS, an interruption that takes every remaining over.
        if (reason == InningsEndReason.Abandoned && !innings.IsSuperOver
            && DlsStandardEdition.Applies(match.Rules))
        {
            RecordInterruption(innings, innings.LegalBalls);
        }

        innings.Status = InningsStatus.Completed;
        innings.EndReason = reason;
        innings.CompletedAt = DateTime.UtcNow;

        var how = reason switch
        {
            InningsEndReason.AllOut => "all out",
            InningsEndReason.OversComplete => "overs complete",
            InningsEndReason.TargetChased => "target reached",
            InningsEndReason.Declared => "declared",
            _ => "abandoned"
        };

        Log(match, reason == InningsEndReason.Declared
                ? CricketMatchEventType.Declared
                : CricketMatchEventType.InningsEnded,
            $"{innings.BattingTeamName} finished on {innings.ScoreLine} ({how}).",
            innings.BattingTeamId);

        EvaluateAfterInnings(match, innings);
    }

    // -----------------------------------------------------------------
    // Deciding the match
    // -----------------------------------------------------------------

    private void EvaluateAfterInnings(CricketMatch match, CricketInnings justEnded)
    {
        if (justEnded.IsSuperOver)
        {
            EvaluateSuperOver(match);
            return;
        }

        var first = match.TeamBattingFirstId!.Value;
        var second = OtherTeam(match, first);
        var perSide = match.Rules.InningsPerSide;

        // A chase that got there is over, and it is over by wickets.
        if (justEnded.EndReason == InningsEndReason.TargetChased)
        {
            WinByWickets(match, justEnded);
            return;
        }

        // A rain-affected chase is settled against its revised target, not the raw aggregate.
        if (TryDecideByDls(match, justEnded)) return;

        if (CompletedInningsFor(match, first) >= perSide && CompletedInningsFor(match, second) >= perSide)
        {
            DecideOnAggregate(match, first, second);
            return;
        }

        // One side has batted out all its innings and is still behind: there is nothing left to
        // play for, and the other side never had to bat again.
        foreach (var (batted, other) in new[] { (first, second), (second, first) })
        {
            if (CompletedInningsFor(match, batted) < perSide) continue;

            var margin = Aggregate(match, other) - Aggregate(match, batted);
            if (margin > 0)
            {
                Win(match, other, $"{TeamName(match, other)} won by an innings and {margin} run{Plural(margin)}");
                return;
            }
        }

        match.Status = CricketMatchStatus.InningsBreak;
    }

    private static int Aggregate(CricketMatch match, int teamId) =>
        match.Innings.Where(i => !i.IsSuperOver && i.BattingTeamId == teamId).Sum(i => i.Runs);

    private static int CompletedInningsFor(CricketMatch match, int teamId) =>
        match.Innings.Count(i => !i.IsSuperOver && i.BattingTeamId == teamId && i.IsComplete);

    /// <summary>Every innings has been played out. Whoever has more runs has won.</summary>
    private void DecideOnAggregate(CricketMatch match, int first, int second)
    {
        var runsFirst = Aggregate(match, first);
        var runsSecond = Aggregate(match, second);

        if (runsFirst == runsSecond)
        {
            ResolveTie(match, first, second);
            return;
        }

        var winner = runsFirst > runsSecond ? first : second;
        var margin = Math.Abs(runsFirst - runsSecond);
        Win(match, winner, $"{TeamName(match, winner)} won by {margin} run{Plural(margin)}");
    }

    private void WinByWickets(CricketMatch match, CricketInnings chase)
    {
        var wickets = Math.Max(0, chase.WicketsToEndInnings - chase.Wickets);
        var summary = $"{chase.BattingTeamName} won by {wickets} wicket{Plural(wickets)}";

        if (chase.BallsRemaining is int balls && balls > 0)
            summary += $" ({balls} ball{Plural(balls)} remaining)";

        if (TargetWasRevised(match, chase))
        {
            summary += " (DLS method)";
            match.WonByDls = true;
        }

        Win(match, chase.BattingTeamId, summary);
    }

    private void Win(CricketMatch match, int teamId, string summary)
    {
        match.WinnerTeamId = teamId;
        match.IsTie = match.IsDraw = match.IsNoResult = false;
        match.ResultSummary = summary;
        match.Status = CricketMatchStatus.Completed;
        match.CompletedAt = DateTime.UtcNow;
        Log(match, CricketMatchEventType.MatchCompleted, summary + ".", teamId);
    }

    /// <summary>
    /// Scores level. What happens next is the league's choice, not the laws' — some play a super
    /// over, some count boundaries, some simply share it.
    /// </summary>
    private void ResolveTie(CricketMatch match, int first, int second)
    {
        switch (match.Rules.TieResolution)
        {
            case TieResolution.SuperOver:
                match.Status = CricketMatchStatus.SuperOver;
                match.ResultSummary = "Scores level — super over to come";
                break;

            case TieResolution.BoundaryCount:
                var boundariesFirst = BoundaryCount(match, first);
                var boundariesSecond = BoundaryCount(match, second);
                if (boundariesFirst == boundariesSecond)
                {
                    DeclareTie(match);
                }
                else
                {
                    var winner = boundariesFirst > boundariesSecond ? first : second;
                    var count = Math.Max(boundariesFirst, boundariesSecond);
                    Win(match, winner, $"{TeamName(match, winner)} won on boundary count ({count})");
                }
                break;

            case TieResolution.Bowlout:
                // Not modelled ball by ball; the scorer records the winner through Complete.
                match.Status = CricketMatchStatus.InningsBreak;
                match.ResultSummary = "Scores level — bowl-out required";
                break;

            default:
                DeclareTie(match);
                break;
        }
    }

    private void DeclareTie(CricketMatch match)
    {
        match.IsTie = true;
        match.WinnerTeamId = null;
        match.ResultSummary = "Match tied";
        match.Status = CricketMatchStatus.Completed;
        match.CompletedAt = DateTime.UtcNow;
        Log(match, CricketMatchEventType.MatchCompleted, "Match tied.");
    }

    private static int BoundaryCount(CricketMatch match, int teamId) =>
        match.Innings
            .Where(i => i.BattingTeamId == teamId)
            .SelectMany(i => i.Balls)
            .Count(b => b.IsFour || b.IsSix);

    /// <summary>
    /// A super over is scored by the same engine as anything else, so all that is left here is
    /// comparing the pair and deciding whether another one is needed.
    /// </summary>
    private void EvaluateSuperOver(CricketMatch match)
    {
        var supers = match.Innings.Where(i => i.IsSuperOver).OrderBy(i => i.InningsNumber).ToList();

        // A super over is a pair of innings. Nothing is decided until both sides have batted in the
        // current one — comparing across pairs is what used to hand the match to the side that
        // batted last in the previous (tied) super over before the other side had batted.
        if (supers.Count == 0 || supers.Count % 2 == 1 || !supers[^1].IsComplete || !supers[^2].IsComplete)
        {
            match.Status = CricketMatchStatus.SuperOver;
            return;
        }

        var a = supers[^2];
        var b = supers[^1];

        if (a.Runs == b.Runs)
        {
            // Still level. Another super over, unless the league would rather share it.
            if (match.Rules.TieResolution == TieResolution.SuperOver)
            {
                match.Status = CricketMatchStatus.SuperOver;
                match.ResultSummary = "Super over tied — another to come";
            }
            else
            {
                DeclareTie(match);
            }
            return;
        }

        var winning = a.Runs > b.Runs ? a : b;
        Win(match, winning.BattingTeamId, $"{winning.BattingTeamName} won the super over");
    }

    // -----------------------------------------------------------------
    // The follow-on and the super over
    // -----------------------------------------------------------------

    public async Task<CricketResult<CricketMatch>> EnforceFollowOnAsync(int matchId)
    {
        var match = await LoadAsync(matchId);
        if (match == null) return CricketResult<CricketMatch>.Missing("Match not found.");

        if (!match.Rules.IsMultiInnings)
            return CricketResult<CricketMatch>.Fail("There is no follow-on in a one-innings match.");

        if (match.Rules.FollowOnMargin is not int required)
            return CricketResult<CricketMatch>.Fail("This match does not play the follow-on.");

        var main = match.Innings.Where(i => !i.IsSuperOver).ToList();
        if (main.Count != 2 || main.Any(i => !i.IsComplete))
            return CricketResult<CricketMatch>.Fail("The follow-on can only be enforced after both sides have batted once.");

        if (match.FollowOnEnforced)
            return CricketResult<CricketMatch>.Fail("The follow-on has already been enforced.");

        var first = match.TeamBattingFirstId!.Value;
        var second = OtherTeam(match, first);
        var lead = Aggregate(match, first) - Aggregate(match, second);

        if (lead < required)
            return CricketResult<CricketMatch>.Fail(
                $"A lead of {required} is needed to enforce the follow-on; the lead is {lead}.");

        match.FollowOnEnforced = true;
        Log(match, CricketMatchEventType.FollowOnEnforced,
            $"{TeamName(match, first)} enforced the follow-on, {lead} runs ahead.", first);

        await _unitOfWork.SaveChangesAsync();
        return CricketResult<CricketMatch>.Success(match);
    }

    public async Task<CricketResult<CricketMatch>> StartSuperOverAsync(int matchId)
    {
        var match = await LoadAsync(matchId);
        if (match == null) return CricketResult<CricketMatch>.Missing("Match not found.");

        if (match.Status == CricketMatchStatus.Completed)
            return CricketResult<CricketMatch>.Fail("This match is over.");

        if (match.Innings.Any(i => i.Status == InningsStatus.InProgress))
            return CricketResult<CricketMatch>.Fail("An innings is still under way.");

        var first = match.TeamBattingFirstId;
        if (first is null) return CricketResult<CricketMatch>.Fail("The match has not been set up.");

        var second = OtherTeam(match, first.Value);
        var supers = match.Innings.Where(i => i.IsSuperOver).ToList();

        if (supers.Count % 2 == 1)
            return CricketResult<CricketMatch>.Fail("The current super over isn't finished — both sides bat once.");

        // Legitimate only from level scores: either the match itself was tied, or the last pair of
        // super overs was.
        var level = supers.Count >= 2
            ? supers[^1].Runs == supers[^2].Runs
            : Aggregate(match, first.Value) == Aggregate(match, second);

        if (!level)
            return CricketResult<CricketMatch>.Fail("A super over is only played when the scores are level.");

        match.Status = CricketMatchStatus.SuperOver;
        match.ResultSummary = null;
        Log(match, CricketMatchEventType.SuperOverStarted, "A super over will decide it.");

        await _unitOfWork.SaveChangesAsync();
        return CricketResult<CricketMatch>.Success(match);
    }

    // -----------------------------------------------------------------
    // Signing it off
    // -----------------------------------------------------------------

    public async Task<CricketResult<CricketMatch>> CompleteAsync(int matchId, CompleteCricketMatchRequest request)
    {
        var match = await LoadAsync(matchId);
        if (match == null) return CricketResult<CricketMatch>.Missing("Match not found.");

        if (match.Status == CricketMatchStatus.Completed && !request.Force
            && request.AwardWinnerTeamId is null && !request.NoResult)
            return CricketResult<CricketMatch>.Success(match);

        if (request.AwardWinnerTeamId is int awarded)
        {
            if (awarded != match.HomeTeamId && awarded != match.AwayTeamId)
                return CricketResult<CricketMatch>.Fail("The winner must be one of the two teams.");

            match.ForfeitWinnerTeamId = awarded;
            Win(match, awarded, $"{TeamName(match, awarded)} won (awarded)");
            await _unitOfWork.SaveChangesAsync();
            return CricketResult<CricketMatch>.Success(match);
        }

        if (request.NoResult)
        {
            match.IsNoResult = true;
            match.IsTie = match.IsDraw = false;
            match.WinnerTeamId = null;
            match.ResultSummary = "No result";
            match.Status = CricketMatchStatus.Completed;
            match.CompletedAt = DateTime.UtcNow;
            Log(match, CricketMatchEventType.MatchAbandoned, "Abandoned — no result.");
            await _unitOfWork.SaveChangesAsync();
            return CricketResult<CricketMatch>.Success(match);
        }

        if (!request.Force)
            return CricketResult<CricketMatch>.Fail("The match has not been decided. Play on, or sign it off with force.");

        // Forced: a timed match that ran out of time is a draw, which is a real result. Anything
        // else falls back on the runs actually scored.
        foreach (var innings in match.Innings.Where(i => i.Status == InningsStatus.InProgress))
            CloseInnings(match, innings, InningsEndReason.Abandoned);

        if (match.Status != CricketMatchStatus.Completed)
        {
            if (match.Rules.DrawAllowed)
            {
                match.IsDraw = true;
                match.WinnerTeamId = null;
                match.ResultSummary = "Match drawn";
                match.Status = CricketMatchStatus.Completed;
                match.CompletedAt = DateTime.UtcNow;
                Log(match, CricketMatchEventType.MatchCompleted, "Match drawn.");
            }
            else
            {
                var first = match.TeamBattingFirstId;
                if (first is null)
                {
                    match.IsNoResult = true;
                    match.ResultSummary = "No result";
                    match.Status = CricketMatchStatus.Completed;
                    match.CompletedAt = DateTime.UtcNow;
                }
                else
                {
                    DecideOnAggregate(match, first.Value, OtherTeam(match, first.Value));
                }
            }
        }

        await _unitOfWork.SaveChangesAsync();
        return CricketResult<CricketMatch>.Success(match);
    }

    private static string Plural(int n) => n == 1 ? "" : "s";
}
