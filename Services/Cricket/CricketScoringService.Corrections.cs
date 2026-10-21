using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services.Cricket;

/// <summary>
/// Correcting a player picked by mistake — the scorer tapped the wrong opener, or the wrong name for
/// this over's bowler. Nothing that happened changes: the same runs, the same wickets, the same
/// ends. Only the name on those deliveries does, so the totals stay as they are and the scorecard,
/// awards and stats (all read off the ledger) follow by themselves.
/// <list type="bullet">
/// <item><b>A batter at the crease</b>: their whole stay in this innings moves to someone in the XI
/// who hasn't batted in it yet.</item>
/// <item><b>The bowler of the over in progress</b>: that over moves to someone allowed to bowl it
/// (not the last over's bowler, not over their limit). Earlier overs stay as they are.</item>
/// </list>
/// Only while the innings is being played; never an earlier innings or a finished match.
/// </summary>
public partial class CricketScoringService
{
    public async Task<CricketResult<CricketMatch>> ReplacePlayerAsync(int matchId, ReplacePlayerRequest request)
    {
        var match = await LoadAsync(matchId);
        if (match == null) return CricketResult<CricketMatch>.Missing("Match not found.");

        var innings = CurrentInnings(match);
        if (innings == null || innings.Status != InningsStatus.InProgress)
            return CricketResult<CricketMatch>.Fail("Players can only be corrected while an innings is being played.");

        var wrong = request.PlayerId;
        var right = request.ReplacementId;
        if (wrong == right) return CricketResult<CricketMatch>.Fail("Choose a different player.");

        string description;
        if (wrong == innings.StrikerId || wrong == innings.NonStrikerId)
        {
            if (!IsPlaying(match, right, innings.BattingTeamId))
                return CricketResult<CricketMatch>.Fail("That player is not in the batting side's XI.");
            if (Appeared(innings).Contains(right))
                return CricketResult<CricketMatch>.Fail($"{NameOf(match, right)} has already batted in this innings.");

            ReplaceBatter(innings, wrong, right);
            var inningsNumber = innings.InningsNumber;
            foreach (var e in await _events.ListAsync(x => x.CricketMatchId == matchId && x.EventType == CricketMatchEventType.BatterIn
                                                           && x.InningsNumber == inningsNumber && x.PlayerId == wrong))
            {
                e.PlayerId = right;
                e.Description = $"{NameOf(match, right)} came in.";
            }
            description = $"Correction: {NameOf(match, right)} batted in place of {NameOf(match, wrong)}.";
            Log(match, CricketMatchEventType.PlayerReplaced, description, innings.BattingTeamId, right);
        }
        else if (wrong == innings.CurrentBowlerId)
        {
            if (BowlerProblem(match, innings, right) is { } problem)
                return CricketResult<CricketMatch>.Fail(problem);

            ReplaceBowler(innings, wrong, right);
            var inningsNumber = innings.InningsNumber;
            var cameOn = (await _events.ListAsync(x => x.CricketMatchId == matchId && x.EventType == CricketMatchEventType.BowlerChanged
                                                       && x.InningsNumber == inningsNumber && x.PlayerId == wrong))
                .MaxBy(e => e.Id);
            if (cameOn != null)
            {
                cameOn.PlayerId = right;
                cameOn.Description = $"{NameOf(match, right)} came on to bowl.";
            }
            description = $"Correction: {NameOf(match, right)} is bowling over {CurrentOverNumber(innings)} in place of {NameOf(match, wrong)}.";
            Log(match, CricketMatchEventType.PlayerReplaced, description, innings.BowlingTeamId, right);
        }
        else
        {
            return CricketResult<CricketMatch>.Fail("Only the batters at the crease and the bowler of this over can be corrected.");
        }

        await _unitOfWork.SaveChangesAsync();
        return CricketResult<CricketMatch>.Success(match);
    }

    /// <summary>Everyone who has been to the crease in this innings, including whoever is there now.</summary>
    private static HashSet<int> Appeared(CricketInnings innings)
    {
        var seen = new HashSet<int>();
        foreach (var id in new[] { innings.OpeningStrikerId, innings.OpeningNonStrikerId, innings.StrikerId, innings.NonStrikerId })
            if (id is int p) seen.Add(p);
        foreach (var ball in innings.Balls)
        {
            seen.Add(ball.StrikerId);
            if (ball.NonStrikerId is int partner) seen.Add(partner);
            if (ball.DismissedPlayerId is int gone) seen.Add(gone);
        }
        return seen;
    }

    /// <summary>The over being bowled now (1-based). A bowler is only ever named for the next ball's over.</summary>
    private static int CurrentOverNumber(CricketInnings innings) => innings.LegalBalls / innings.BallsPerOver + 1;

    /// <summary>Why <paramref name="playerId"/> can't take over this over, or null.</summary>
    private static string? BowlerProblem(CricketMatch match, CricketInnings innings, int playerId)
    {
        if (!IsPlaying(match, playerId, innings.BowlingTeamId))
            return "That player is not in the fielding side's XI.";
        if (playerId == innings.PreviousBowlerId)
            return $"{NameOf(match, playerId)} bowled the last over, and a bowler may not bowl two overs in a row.";
        if (innings.MaxOversPerBowler is int max && CompletedOversBy(innings, playerId) >= max)
            return $"{NameOf(match, playerId)} has bowled the maximum of {max} overs.";
        return null;
    }

    /// <summary>Renames a batter throughout this innings: on strike, at the other end, out.</summary>
    private static void ReplaceBatter(CricketInnings innings, int wrong, int right)
    {
        if (innings.OpeningStrikerId == wrong) innings.OpeningStrikerId = right;
        if (innings.OpeningNonStrikerId == wrong) innings.OpeningNonStrikerId = right;
        if (innings.StrikerId == wrong) innings.StrikerId = right;
        if (innings.NonStrikerId == wrong) innings.NonStrikerId = right;
        foreach (var ball in innings.Balls)
        {
            if (ball.StrikerId == wrong) ball.StrikerId = right;
            if (ball.NonStrikerId == wrong) ball.NonStrikerId = right;
            if (ball.DismissedPlayerId == wrong) ball.DismissedPlayerId = right;
        }
    }

    /// <summary>
    /// Renames the bowler of the over in progress: its deliveries, and the catches or run-outs the
    /// bowler was recorded as fielder on in it (caught and bowled). Earlier overs are left alone.
    /// </summary>
    private static void ReplaceBowler(CricketInnings innings, int wrong, int right)
    {
        var over = CurrentOverNumber(innings);
        if (innings.CurrentBowlerId == wrong) innings.CurrentBowlerId = right;
        if (over == 1 && innings.OpeningBowlerId == wrong) innings.OpeningBowlerId = right;
        foreach (var ball in innings.Balls.Where(b => b.OverNumber == over && b.BowlerId == wrong))
        {
            ball.BowlerId = right;
            if (ball.FielderId == wrong) ball.FielderId = right;
            if (ball.RunOutReceiverId == wrong) ball.RunOutReceiverId = right;
        }
    }
}
