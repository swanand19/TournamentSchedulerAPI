using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services.Cricket;

/// <summary>
/// The match as a scoring screen sees it — including what is legal next, which is decided here so
/// that no client ever has to know a law to draw a button.
/// </summary>
public partial class CricketScoringService
{
    public CricketMatchStateDto BuildState(CricketMatch match)
    {
        var innings = CurrentInnings(match);
        var live = innings is { Status: InningsStatus.InProgress } ? BuildLiveState(match, innings) : null;

        var state = new CricketMatchStateDto
        {
            Id = match.Id,
            TournamentId = match.TournamentId,
            GroupName = match.GroupName,
            MatchNumber = match.MatchNumber,
            HomeTeamId = match.HomeTeamId,
            AwayTeamId = match.AwayTeamId,
            HomeTeamName = match.HomeTeamName,
            AwayTeamName = match.AwayTeamName,
            Status = match.Status.ToString(),
            Rules = match.Rules,
            TossWinnerTeamId = match.TossWinnerTeamId,
            TossDecision = match.TossDecision?.ToString(),
            TeamBattingFirstId = match.TeamBattingFirstId,
            TossSummary = match.TossWinnerTeamId is int winner && match.TossDecision is { } decision
                ? $"{TeamName(match, winner)} won the toss and chose to " +
                  (decision == TossDecision.Bat ? "bat" : "bowl")
                : null,
            IsSetUp = match.IsSetUp,
            MaxInnings = match.MaxInnings,
            CurrentInningsNumber = match.CurrentInningsNumber,
            FollowOnEnforced = match.FollowOnEnforced,
            WinnerTeamId = match.WinnerTeamId,
            IsTie = match.IsTie,
            IsDraw = match.IsDraw,
            IsNoResult = match.IsNoResult,
            ResultSummary = match.ResultSummary,
            Squad = match.Squad.Select(ToMember).ToList(),
            Innings = match.Innings.OrderBy(i => i.InningsNumber).Select(ToSummary).ToList(),
            Current = live
        };

        state.Actions = BuildActions(match, innings);
        return state;
    }

    private static CricketSquadMember ToMember(CricketMatchPlayer player) => new()
    {
        PlayerId = player.PlayerId,
        TeamId = player.TeamId,
        PlayerName = player.PlayerName,
        SquadStatus = player.SquadStatus.ToString(),
        IsCaptain = player.IsCaptain,
        IsWicketKeeper = player.IsWicketKeeper,
        BattingOrder = player.BattingOrder
    };

    private static InningsSummary ToSummary(CricketInnings innings) => new()
    {
        Id = innings.Id,
        InningsNumber = innings.InningsNumber,
        BattingTeamId = innings.BattingTeamId,
        BattingTeamName = innings.BattingTeamName,
        BowlingTeamName = innings.BowlingTeamName,
        Runs = innings.Runs,
        Wickets = innings.Wickets,
        Overs = innings.OversText,
        OversLimit = innings.OversLimit,
        Target = innings.Target,
        Status = innings.Status.ToString(),
        EndReason = innings.EndReason?.ToString(),
        IsSuperOver = innings.IsSuperOver,
        IsFollowOn = innings.IsFollowOn,
        ScoreLine = innings.ScoreLine,
        RunRate = innings.RunRate
    };

    private static InningsLiveState BuildLiveState(CricketMatch match, CricketInnings innings)
    {
        var balls = innings.Balls.OrderBy(b => b.SequenceNumber).ToList();
        var currentOver = innings.LegalBalls / innings.BallsPerOver + 1;

        // Between overs the new one is empty, and a blank strip is the one moment a scorer most
        // wants to see what just happened — so keep showing the over that has only just finished.
        var overToShow = currentOver > 1 && balls.All(b => b.OverNumber != currentOver)
            ? currentOver - 1
            : currentOver;

        var (partnershipRuns, partnershipBalls) = CurrentPartnership(balls);

        return new InningsLiveState
        {
            Id = innings.Id,
            InningsNumber = innings.InningsNumber,
            BattingTeamId = innings.BattingTeamId,
            BowlingTeamId = innings.BowlingTeamId,
            BattingTeamName = innings.BattingTeamName,
            Runs = innings.Runs,
            Wickets = innings.Wickets,
            Overs = innings.OversText,
            OversLimit = innings.OversLimit,
            ScoreLine = innings.ScoreLine,
            RunRate = innings.RunRate,
            Target = innings.Target,
            RunsRequired = innings.RunsRequired,
            BallsRemaining = innings.BallsRemaining,
            RequiredRunRate = innings.RequiredRunRate,
            ExtrasTotal = innings.ExtrasTotal,
            FreeHitPending = innings.FreeHitPending,
            Striker = BuildBatter(match, innings, balls, innings.StrikerId, onStrike: true),
            NonStriker = BuildBatter(match, innings, balls, innings.NonStrikerId, onStrike: false),
            Bowler = BuildBowler(match, innings, balls, innings.CurrentBowlerId),
            PreviousBowlerId = innings.PreviousBowlerId,
            ThisOver = balls.Where(b => b.OverNumber == overToShow).Select(ToBallSummary).ToList(),
            PartnershipRuns = partnershipRuns,
            PartnershipBalls = partnershipBalls
        };
    }

    private static CreaseBatter? BuildBatter(
        CricketMatch match, CricketInnings innings, List<CricketBall> balls, int? playerId, bool onStrike)
    {
        if (playerId is not int id) return null;

        var faced = balls.Where(b => b.StrikerId == id).ToList();
        return new CreaseBatter
        {
            PlayerId = id,
            PlayerName = NameOf(match, id),
            Runs = faced.Sum(b => b.RunsOffBat),
            BallsFaced = faced.Count(b => !b.IsWide),
            Fours = faced.Count(b => b.IsFour),
            Sixes = faced.Count(b => b.IsSix),
            OnStrike = onStrike
        };
    }

    private static CreaseBowler? BuildBowler(
        CricketMatch match, CricketInnings innings, List<CricketBall> balls, int? playerId)
    {
        if (playerId is not int id) return null;

        var bowled = balls.Where(b => b.BowlerId == id).ToList();
        var legal = bowled.Count(b => b.IsLegalDelivery);

        return new CreaseBowler
        {
            PlayerId = id,
            PlayerName = NameOf(match, id),
            Overs = $"{legal / innings.BallsPerOver}.{legal % innings.BallsPerOver}",
            Maidens = CountMaidens(bowled, innings.BallsPerOver),
            Runs = bowled.Sum(b => b.RunsChargedToBowler),
            Wickets = bowled.Count(b => b.CreditsBowler)
        };
    }

    /// <summary>The over written out the way a scorer writes it: 1 · 4 · wd · W · 2 · nb1.</summary>
    private static BallSummary ToBallSummary(CricketBall ball)
    {
        string display;
        if (ball.IsWide)
            display = ball.WideExtraRuns > 0 ? $"wd{ball.WideExtraRuns}" : "wd";
        else if (ball.IsNoBall)
        {
            var off = ball.RunsOffBat + ball.Byes + ball.LegByes;
            display = off > 0 ? $"nb{off}" : "nb";
        }
        else if (ball.Byes > 0) display = $"{ball.Byes}b";
        else if (ball.LegByes > 0) display = $"{ball.LegByes}lb";
        else display = ball.RunsOffBat.ToString();

        if (ball.CountsAsWicket) display = display is "0" ? "W" : $"{display}+W";

        return new BallSummary
        {
            SequenceNumber = ball.SequenceNumber,
            OverNumber = ball.OverNumber,
            Display = display,
            Runs = ball.TotalRuns,
            IsWicket = ball.CountsAsWicket,
            IsBoundary = ball.IsFour || ball.IsSix,
            IsExtra = !ball.IsLegalDelivery || ball.Byes > 0 || ball.LegByes > 0 || ball.PenaltyRuns > 0,
            IsFreeHit = ball.IsFreeHit
        };
    }

    private CricketActions BuildActions(CricketMatch match, CricketInnings? innings)
    {
        var inningsInProgress = match.Innings.Any(i => i.Status == InningsStatus.InProgress);
        var over = match.Status is CricketMatchStatus.Completed or CricketMatchStatus.Abandoned;
        var mainPlayed = match.Innings.Count(i => !i.IsSuperOver);

        var actions = new CricketActions
        {
            CanSetUp = match.Status == CricketMatchStatus.NotStarted,
            CanStartInnings = match.IsSetUp && !over && !inningsInProgress
                              && (mainPlayed < match.MaxInnings || match.Status == CricketMatchStatus.SuperOver),
            CanRecordBall = innings?.CanRecordBall ?? false,
            CanUndo = innings is { Balls.Count: > 0 },
            NeedsBatter = innings?.NeedsBatter ?? false,
            NeedsBowler = innings?.NeedsBowler ?? false,
            CanEndInnings = innings is { Status: InningsStatus.InProgress },
            CanDeclare = innings is { Status: InningsStatus.InProgress } && match.Rules.IsMultiInnings,
            CanStartSuperOver = match.Status == CricketMatchStatus.SuperOver && !inningsInProgress,
            CanComplete = !over,
            CanEnforceFollowOn = CanEnforceFollowOn(match)
        };

        if (actions.CanStartInnings && match.TeamBattingFirstId.HasValue)
        {
            var next = NextBattingTeam(match, match.Status == CricketMatchStatus.SuperOver);
            actions.NextBattingTeamId = next;
            actions.NextBowlingTeamId = OtherTeam(match, next);
        }

        if (innings is { Status: InningsStatus.InProgress })
        {
            actions.AvailableBatters = match.Squad
                .Where(p => p.TeamId == innings.BattingTeamId
                            && p.SquadStatus == CricketSquadStatus.Playing
                            && p.PlayerId != innings.StrikerId
                            && p.PlayerId != innings.NonStrikerId
                            && !IsOut(innings, p.PlayerId))
                .OrderBy(p => p.BattingOrder ?? int.MaxValue)
                .ThenBy(p => p.PlayerName)
                .Select(ToMember)
                .ToList();

            actions.AvailableBowlers = match.Squad
                .Where(p => p.TeamId == innings.BowlingTeamId
                            && p.SquadStatus == CricketSquadStatus.Playing
                            && p.PlayerId != innings.PreviousBowlerId
                            && (innings.MaxOversPerBowler is not int max
                                || CompletedOversBy(innings, p.PlayerId) < max))
                .OrderBy(p => p.PlayerName)
                .Select(ToMember)
                .ToList();

            actions.PossibleDismissals = Enum.GetValues<DismissalType>()
                .Where(d => d != DismissalType.LBW || match.Rules.LbwEnabled)
                .Where(d => !innings.FreeHitPending || DismissalRules.PossibleOnFreeHit(d))
                .Select(d => d.ToString())
                .ToList();
        }

        return actions;
    }

    private static bool CanEnforceFollowOn(CricketMatch match)
    {
        if (!match.Rules.IsMultiInnings || match.FollowOnEnforced) return false;
        if (match.Rules.FollowOnMargin is not int required) return false;
        if (match.TeamBattingFirstId is not int first) return false;

        var main = match.Innings.Where(i => !i.IsSuperOver).ToList();
        if (main.Count != 2 || main.Any(i => !i.IsComplete)) return false;

        var lead = Aggregate(match, first) - Aggregate(match, OtherTeam(match, first));
        return lead >= required;
    }
}
