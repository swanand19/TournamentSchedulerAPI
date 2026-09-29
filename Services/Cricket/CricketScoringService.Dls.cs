using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services.Cricket;

/// <summary>
/// Rain: overs coming off an innings, and — when the match plays Duckworth-Lewis-Stern — what that
/// does to the target, the par score and the result.
///
/// The arithmetic lives in <see cref="DlsStandardEdition"/>; this is where it meets the match.
/// Every overs cut is recorded as a <see cref="CricketInterruption"/> at the ball it happened on,
/// because the method prices lost overs by the wickets in hand at that moment.
/// </summary>
public partial class CricketScoringService
{
    public async Task<CricketResult<CricketMatch>> ReduceOversAsync(int matchId, ReduceOversRequest request)
    {
        var match = await LoadAsync(matchId);
        if (match == null) return CricketResult<CricketMatch>.Missing("Match not found.");

        var innings = CurrentInnings(match);
        if (innings == null || innings.Status != InningsStatus.InProgress)
            return CricketResult<CricketMatch>.Fail("No innings is under way.");

        if (innings.IsSuperOver)
            return CricketResult<CricketMatch>.Fail("A super over cannot be shortened.");

        if (innings.OversLimit is not int current)
            return CricketResult<CricketMatch>.Fail("This innings has no over limit to reduce.");

        if (request.NewOversLimit < 1)
            return CricketResult<CricketMatch>.Fail("An innings needs at least one over.");

        if (request.NewOversLimit >= current)
            return CricketResult<CricketMatch>.Fail($"The innings is already limited to {current} overs; a reduction must be fewer.");

        var newBalls = request.NewOversLimit * innings.BallsPerOver;
        if (newBalls < innings.LegalBalls)
            return CricketResult<CricketMatch>.Fail(
                $"{innings.OversText} overs have already been bowled; the innings cannot be cut to fewer.");

        RecordInterruption(innings, newBalls);

        // Bowlers lose overs in proportion, as the playing conditions do (a fifth of the overs in
        // a T20 or a one-day match).
        if (innings.MaxOversPerBowler is int maxEach)
            innings.MaxOversPerBowler = Math.Max(1, (int)Math.Ceiling(maxEach * request.NewOversLimit / (double)current));

        innings.OversLimit = request.NewOversLimit;
        ReviseTargetForDls(match, innings);

        Log(match, CricketMatchEventType.OversReduced,
            $"Overs reduced: {innings.BattingTeamName}'s innings is now {request.NewOversLimit} overs" +
            (innings.Target is int t && IsDlsChase(match, innings) ? $", target revised to {t}." : "."),
            innings.BattingTeamId);

        CloseInningsIfFinished(match, innings);

        await _unitOfWork.SaveChangesAsync();
        return CricketResult<CricketMatch>.Success(match);
    }

    private static void RecordInterruption(CricketInnings innings, int ballsAllowedAfter)
    {
        if (innings.BallsAllowed is not int before || ballsAllowedAfter >= before) return;

        innings.Interruptions.Add(new CricketInterruption
        {
            InningsId = innings.Id,
            AtLegalBalls = innings.LegalBalls,
            WicketsAtTime = innings.Wickets,
            BallsAllowedBefore = before,
            BallsAllowedAfter = ballsAllowedAfter
        });
    }

    /// <summary>The side batting second in a match Duckworth-Lewis-Stern governs.</summary>
    private static bool IsDlsChase(CricketMatch match, CricketInnings innings) =>
        DlsStandardEdition.Applies(match.Rules)
        && !innings.IsSuperOver
        && innings.InningsNumber == 2;

    private static CricketInnings? FirstInnings(CricketMatch match) =>
        match.Innings.FirstOrDefault(i => !i.IsSuperOver && i.InningsNumber == 1);

    /// <summary>
    /// The chase target from the resources each side had. Uninterrupted, both sides have the same
    /// and this is simply one more than team 1's score.
    /// </summary>
    private static int? DlsTarget(CricketMatch match, CricketInnings chase)
    {
        var first = FirstInnings(match);
        if (first == null) return null;

        var r1 = DlsStandardEdition.ResourcesAvailable(first);
        var r2 = DlsStandardEdition.ResourcesAvailable(chase);
        return DlsStandardEdition.TargetFor(first.Runs, r1, r2);
    }

    private static void ReviseTargetForDls(CricketMatch match, CricketInnings innings)
    {
        if (!IsDlsChase(match, innings)) return;
        if (DlsTarget(match, innings) is int target) innings.Target = target;
    }

    private static bool TargetWasRevised(CricketMatch match, CricketInnings chase) =>
        IsDlsChase(match, chase)
        && FirstInnings(match) is { } first
        && chase.Target is int target
        && target != first.Runs + 1;

    /// <summary>
    /// The chase is over without the target being reached, in a DLS match whose target moved — or
    /// play stopped for good during it. Decided on the (possibly just revised) target.
    /// Returns false when this is an ordinary finish the aggregate rules already handle.
    /// </summary>
    private bool TryDecideByDls(CricketMatch match, CricketInnings chase)
    {
        if (!IsDlsChase(match, chase)) return false;

        var abandoned = chase.EndReason == InningsEndReason.Abandoned;
        if (!abandoned && !TargetWasRevised(match, chase)) return false;

        var first = FirstInnings(match)!;

        if (abandoned)
        {
            var minimumBalls = DlsStandardEdition.MinimumOversForResult(match.Rules.OversPerInnings!.Value)
                               * chase.BallsPerOver;
            if (chase.LegalBalls < minimumBalls)
            {
                DeclareNoResult(match, "No result — too few overs bowled for a DLS result");
                return true;
            }

            // Play is over: whatever resources were left are gone, so the target is now the par
            // score at the moment it stopped.
            ReviseTargetForDls(match, chase);
        }

        var par = chase.Target!.Value - 1;
        match.WonByDls = true;

        if (chase.Runs == par)
        {
            // Washed out level: nothing more can be played, so it is a tie. Level after a full
            // (revised) chase goes to whatever the league does with a tie, super over included.
            if (abandoned)
            {
                DeclareTie(match);
                match.ResultSummary = "Match tied (DLS method)";
            }
            else
            {
                ResolveTie(match, first.BattingTeamId, chase.BattingTeamId);
            }
            return true;
        }

        if (chase.Runs > par)
        {
            var margin = chase.Runs - par;
            Win(match, chase.BattingTeamId,
                $"{chase.BattingTeamName} won by {margin} run{Plural(margin)} (DLS method)");
        }
        else
        {
            var margin = par - chase.Runs;
            Win(match, first.BattingTeamId,
                $"{first.BattingTeamName} won by {margin} run{Plural(margin)} (DLS method)");
        }

        return true;
    }

    private void DeclareNoResult(CricketMatch match, string summary)
    {
        match.IsNoResult = true;
        match.IsTie = match.IsDraw = false;
        match.WinnerTeamId = null;
        match.ResultSummary = summary;
        match.Status = CricketMatchStatus.Completed;
        match.CompletedAt = DateTime.UtcNow;
        Log(match, CricketMatchEventType.MatchAbandoned, summary + ".");
    }

    /// <summary>The live DLS picture for the scoring screen, recomputed on every ball.</summary>
    private static CricketDlsState? BuildDlsState(CricketMatch match)
    {
        if (!DlsStandardEdition.Applies(match.Rules)) return null;

        var first = FirstInnings(match);
        if (first == null) return null;

        var chase = match.Innings.FirstOrDefault(i => !i.IsSuperOver && i.InningsNumber == 2);
        var r1 = DlsStandardEdition.ResourcesAvailable(first);
        var minimumOvers = DlsStandardEdition.MinimumOversForResult(match.Rules.OversPerInnings!.Value);

        var state = new CricketDlsState
        {
            G50 = DlsStandardEdition.G50,
            Team1Score = first.Runs,
            Team1Resources = Math.Round(r1, 1),
            MinimumOversForResult = minimumOvers,
            Interruptions = match.Innings
                .Where(i => !i.IsSuperOver)
                .SelectMany(i => i.Interruptions.Select(x => new CricketInterruptionSummary
                {
                    InningsNumber = i.InningsNumber,
                    BattingTeamName = i.BattingTeamName,
                    AtOvers = $"{x.AtLegalBalls / i.BallsPerOver}.{x.AtLegalBalls % i.BallsPerOver}",
                    OversBefore = x.BallsAllowedBefore / i.BallsPerOver,
                    OversAfter = x.BallsAllowedAfter / i.BallsPerOver
                }))
                .ToList()
        };

        if (chase == null) return state;

        var r2 = DlsStandardEdition.ResourcesAvailable(chase);
        state.Team2Resources = Math.Round(r2, 1);
        state.Target = chase.Target;
        state.TargetRevised = TargetWasRevised(match, chase);

        if (chase.Status == InningsStatus.InProgress && chase.BallsAllowed is int allowed)
        {
            var remaining = DlsStandardEdition.Resources(allowed - chase.LegalBalls, chase.Wickets);
            var used = Math.Max(0, r2 - remaining);
            var par = (int)Math.Floor(DlsStandardEdition.ParFor(first.Runs, r1, used) + 1e-9);

            state.Team2ResourcesUsed = Math.Round(used, 1);
            state.Team2ResourcesRemaining = Math.Round(remaining, 1);
            state.ParScore = par;
            state.RunsAheadOfPar = chase.Runs - par;
            state.ResultPossibleNow = chase.LegalBalls >= minimumOvers * chase.BallsPerOver;
        }

        return state;
    }
}
