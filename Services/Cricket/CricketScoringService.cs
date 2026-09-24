using Microsoft.EntityFrameworkCore;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services.Cricket;

/// <summary>
/// The laws of cricket, in one place.
///
/// Deliberately a service rather than controller code: football's rules ended up inline in
/// MatchesController and it is now 800 lines, and cricket has an order of magnitude more of them.
/// Everything a screen needs to know about what is legal right now is decided here and travels back
/// on the match, so no client ever re-derives a rule.
///
/// The one structural idea worth knowing before reading on: <see cref="CricketInnings"/> totals are
/// never nudged. Every ball appends to the ledger and the innings is then re-folded from it by
/// <see cref="Recompute"/>. Undo is therefore just "delete the last row and fold again", which is
/// what keeps it exact through the awkward cases — a ball that ended an over, took a wicket and
/// finished the innings all at once.
/// </summary>
public partial class CricketScoringService : ICricketScoringService
{
    private readonly TournamentDbContext _db;

    public CricketScoringService(TournamentDbContext db)
    {
        _db = db;
    }

    // -----------------------------------------------------------------
    // Loading
    // -----------------------------------------------------------------

    public Task<CricketMatch?> GetAsync(int matchId) => LoadAsync(matchId);

    private async Task<CricketMatch?> LoadAsync(int matchId) =>
        await _db.CricketMatches
            .Include(m => m.Squad)
            .Include(m => m.Innings).ThenInclude(i => i.Balls)
            .FirstOrDefaultAsync(m => m.Id == matchId);

    private static CricketInnings? CurrentInnings(CricketMatch match) =>
        match.Innings.FirstOrDefault(i => i.InningsNumber == match.CurrentInningsNumber);

    private static int OtherTeam(CricketMatch match, int teamId) =>
        teamId == match.HomeTeamId ? match.AwayTeamId!.Value : match.HomeTeamId!.Value;

    private static string TeamName(CricketMatch match, int teamId) =>
        teamId == match.HomeTeamId ? match.HomeTeamName : match.AwayTeamName;

    private void Log(CricketMatch match, CricketMatchEventType type, string description,
        int? teamId = null, int? playerId = null)
    {
        match.Events.Add(new CricketMatchEvent
        {
            CricketMatchId = match.Id,
            EventType = type,
            InningsNumber = match.CurrentInningsNumber == 0 ? null : match.CurrentInningsNumber,
            TeamId = teamId,
            PlayerId = playerId,
            Description = description
        });
    }

    // -----------------------------------------------------------------
    // Setup
    // -----------------------------------------------------------------

    public async Task<CricketResult<CricketMatch>> SetupAsync(int matchId, CricketSetupRequest request)
    {
        var match = await LoadAsync(matchId);
        if (match == null) return CricketResult<CricketMatch>.Missing("Match not found.");

        if (match.Status != CricketMatchStatus.NotStarted)
            return CricketResult<CricketMatch>.Fail("This match has already started.");

        if (match.HomeTeamId is null || match.AwayTeamId is null)
            return CricketResult<CricketMatch>.Fail("Both teams must be known before the match can be set up.");

        var rules = request.Rules ?? CricketRulePresets.Create(request.Preset) ?? new CricketMatchRules();
        if (ValidateRules(rules) is { } ruleError)
            return CricketResult<CricketMatch>.Fail(ruleError);

        if (request.TossWinnerTeamId != match.HomeTeamId && request.TossWinnerTeamId != match.AwayTeamId)
            return CricketResult<CricketMatch>.Fail("The toss winner must be one of the two teams.");

        var squadResult = await BuildSquadAsync(match, request.Squad, rules);
        if (squadResult.Error != null) return CricketResult<CricketMatch>.Fail(squadResult.Error);

        match.Rules = rules;
        match.TossWinnerTeamId = request.TossWinnerTeamId;
        match.TossDecision = request.TossDecision;

        _db.CricketMatchPlayers.RemoveRange(match.Squad);
        match.Squad.Clear();
        foreach (var p in squadResult.Value!) match.Squad.Add(p);

        var decision = request.TossDecision == TossDecision.Bat ? "bat" : "bowl";
        Log(match, CricketMatchEventType.TossCompleted,
            $"{TeamName(match, request.TossWinnerTeamId)} won the toss and chose to {decision}.",
            request.TossWinnerTeamId);

        await _db.SaveChangesAsync();
        return CricketResult<CricketMatch>.Success(match);
    }

    private static string? ValidateRules(CricketMatchRules rules)
    {
        if (rules.BallsPerOver < 1) return "There must be at least one ball per over.";
        if (rules.PlayersPerSide < 2) return "A side needs at least two players.";
        if (rules.InningsPerSide is < 1 or > 2) return "A side bats either once or twice.";

        if (rules.OversPerInnings is null && rules.Format != CricketFormat.MultiInningsTimed)
            return "Only a timed match may be played without an over limit.";

        if (rules.OversPerInnings is <= 0) return "The over limit must be positive.";

        if (rules.MaxOversPerBowler is <= 0) return "A bowler's limit must be positive.";

        // A side cannot get through its overs if no bowler is allowed enough of them.
        if (rules.OversPerInnings is int overs && rules.MaxOversPerBowler is int max)
        {
            var bowlersAvailable = rules.PlayersPerSide;
            if (max * bowlersAvailable < overs)
                return $"{bowlersAvailable} bowlers capped at {max} overs cannot bowl {overs} overs.";
        }

        return null;
    }

    /// <summary>
    /// Turns the posted selections into squad rows, rejecting an XI that could not take the field:
    /// the wrong size, a player from the wrong team, or nobody (or two people) holding the armband
    /// or the gloves.
    /// </summary>
    private async Task<(List<CricketMatchPlayer>? Value, string? Error)> BuildSquadAsync(
        CricketMatch match, List<CricketSquadSelection> selections, CricketMatchRules rules)
    {
        if (selections.Count == 0) return (null, "Both XIs must be picked.");

        var duplicates = selections.GroupBy(s => s.PlayerId).Any(g => g.Count() > 1);
        if (duplicates) return (null, "A player cannot be listed twice.");

        var playerIds = selections.Select(s => s.PlayerId).ToList();
        var players = await _db.Players
            .Where(p => playerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        foreach (var selection in selections)
        {
            if (!players.TryGetValue(selection.PlayerId, out var player))
                return (null, $"Player {selection.PlayerId} does not exist.");

            if (player.TeamId != selection.TeamId)
                return (null, $"{player.Name} does not play for that team.");

            if (selection.TeamId != match.HomeTeamId && selection.TeamId != match.AwayTeamId)
                return (null, "A squad was submitted for a team that is not in this match.");
        }

        foreach (var teamId in new[] { match.HomeTeamId!.Value, match.AwayTeamId!.Value })
        {
            var side = selections.Where(s => s.TeamId == teamId).ToList();
            var playing = side.Where(s => s.SquadStatus == CricketSquadStatus.Playing).ToList();

            if (playing.Count != rules.PlayersPerSide)
                return (null, $"{TeamName(match, teamId)} must field exactly {rules.PlayersPerSide} players, not {playing.Count}.");

            if (playing.Count(s => s.IsCaptain) != 1)
                return (null, $"{TeamName(match, teamId)} needs exactly one captain in the XI.");

            if (playing.Count(s => s.IsWicketKeeper) != 1)
                return (null, $"{TeamName(match, teamId)} needs exactly one wicket-keeper in the XI.");
        }

        var squad = selections.Select(s => new CricketMatchPlayer
        {
            CricketMatchId = match.Id,
            PlayerId = s.PlayerId,
            TeamId = s.TeamId,
            PlayerName = players[s.PlayerId].Name,
            SquadStatus = s.SquadStatus,
            IsCaptain = s.SquadStatus == CricketSquadStatus.Playing && s.IsCaptain,
            IsWicketKeeper = s.SquadStatus == CricketSquadStatus.Playing && s.IsWicketKeeper,
            BattingOrder = s.BattingOrder
        }).ToList();

        return (squad, null);
    }

    // -----------------------------------------------------------------
    // Innings
    // -----------------------------------------------------------------

    public async Task<CricketResult<CricketMatch>> StartInningsAsync(int matchId, StartInningsRequest request)
    {
        var match = await LoadAsync(matchId);
        if (match == null) return CricketResult<CricketMatch>.Missing("Match not found.");

        if (!match.IsSetUp)
            return CricketResult<CricketMatch>.Fail("The match must be set up and the toss made first.");

        if (match.Status is CricketMatchStatus.Completed or CricketMatchStatus.Abandoned)
            return CricketResult<CricketMatch>.Fail("This match is over.");

        if (match.Innings.Any(i => i.Status == InningsStatus.InProgress))
            return CricketResult<CricketMatch>.Fail("An innings is already under way.");

        var isSuperOver = match.Status == CricketMatchStatus.SuperOver;
        var played = match.Innings.Count;

        if (!isSuperOver && played >= match.MaxInnings)
            return CricketResult<CricketMatch>.Fail("Every innings of this match has been played.");

        var battingTeamId = NextBattingTeam(match, isSuperOver);
        var bowlingTeamId = OtherTeam(match, battingTeamId);

        var innings = new CricketInnings
        {
            CricketMatchId = match.Id,
            InningsNumber = played + 1,
            BattingTeamId = battingTeamId,
            BowlingTeamId = bowlingTeamId,
            BattingTeamName = TeamName(match, battingTeamId),
            BowlingTeamName = TeamName(match, bowlingTeamId),
            BattingTeamInningsIndex = match.Innings.Count(i => !i.IsSuperOver && i.BattingTeamId == battingTeamId) + 1,
            IsSuperOver = isSuperOver,
            BallsPerOver = match.Rules.BallsPerOver,
            // A super over is one over and two wickets whatever the format says.
            OversLimit = isSuperOver ? 1 : request.OversLimit ?? match.Rules.OversPerInnings,
            WicketsToEndInnings = isSuperOver ? 2 : match.Rules.WicketsToEndInnings,
            BattingSideSize = match.Rules.PlayersPerSide,
            MaxOversPerBowler = isSuperOver ? 1 : match.Rules.MaxOversPerBowler,
            IsFollowOn = !isSuperOver && match.FollowOnEnforced && battingTeamId != match.TeamBattingFirstId
                         && match.Innings.Count(i => !i.IsSuperOver) == 2,
            Status = InningsStatus.InProgress,
            StartedAt = DateTime.UtcNow
        };

        innings.Target = ComputeTarget(match, innings);

        if (ValidateOpeners(match, innings, request) is { } error)
            return CricketResult<CricketMatch>.Fail(error);

        innings.OpeningStrikerId = innings.StrikerId = request.StrikerId;
        innings.OpeningNonStrikerId = innings.NonStrikerId = request.NonStrikerId;
        innings.OpeningBowlerId = innings.CurrentBowlerId = request.BowlerId;

        match.Innings.Add(innings);
        match.CurrentInningsNumber = innings.InningsNumber;
        match.Status = isSuperOver ? CricketMatchStatus.SuperOver : CricketMatchStatus.InProgress;
        match.StartedAt ??= DateTime.UtcNow;

        Log(match, CricketMatchEventType.InningsStarted,
            $"{innings.BattingTeamName} began innings {innings.InningsNumber}" +
            (innings.Target is int t ? $", chasing {t}." : "."),
            battingTeamId);

        await _db.SaveChangesAsync();
        return CricketResult<CricketMatch>.Success(match);
    }

    /// <summary>
    /// Who bats next. Sides alternate, except that enforcing the follow-on makes the side that
    /// trailed bat twice in succession, and a super over is opened by whoever batted second.
    /// </summary>
    private static int NextBattingTeam(CricketMatch match, bool isSuperOver)
    {
        var first = match.TeamBattingFirstId!.Value;
        var second = OtherTeam(match, first);

        if (isSuperOver)
        {
            var superPlayed = match.Innings.Count(i => i.IsSuperOver);
            return superPlayed % 2 == 0 ? second : first;
        }

        var number = match.Innings.Count(i => !i.IsSuperOver) + 1;
        return number switch
        {
            1 => first,
            2 => second,
            3 => match.FollowOnEnforced ? second : first,
            _ => match.FollowOnEnforced ? first : second
        };
    }

    /// <summary>
    /// The score that wins it. Only the side batting last has one: in limited overs that is the
    /// second innings, in a two-innings match the fourth, and in a super over the second.
    /// </summary>
    private static int? ComputeTarget(CricketMatch match, CricketInnings innings)
    {
        if (innings.IsSuperOver)
        {
            var firstSuper = match.Innings.FirstOrDefault(i => i.IsSuperOver);
            return firstSuper == null ? null : firstSuper.Runs + 1;
        }

        var mainInnings = match.Innings.Where(i => !i.IsSuperOver).ToList();
        var isFinalInnings = mainInnings.Count + 1 == match.MaxInnings;
        if (!isFinalInnings) return null;

        var own = mainInnings.Where(i => i.BattingTeamId == innings.BattingTeamId).Sum(i => i.Runs);
        var opponent = mainInnings.Where(i => i.BattingTeamId != innings.BattingTeamId).Sum(i => i.Runs);
        return opponent - own + 1;
    }

    private static string? ValidateOpeners(CricketMatch match, CricketInnings innings, StartInningsRequest request)
    {
        if (request.StrikerId == request.NonStrikerId)
            return "The two batters must be different players.";

        if (!IsPlaying(match, request.StrikerId, innings.BattingTeamId))
            return "The striker is not in the batting side's XI.";

        if (!IsPlaying(match, request.NonStrikerId, innings.BattingTeamId))
            return "The non-striker is not in the batting side's XI.";

        if (!IsPlaying(match, request.BowlerId, innings.BowlingTeamId))
            return "The bowler is not in the fielding side's XI.";

        if (innings.OversLimit is <= 0)
            return "The over limit must be positive.";

        return null;
    }

    private static bool IsPlaying(CricketMatch match, int playerId, int teamId) =>
        match.Squad.Any(p => p.PlayerId == playerId
                             && p.TeamId == teamId
                             && p.SquadStatus == CricketSquadStatus.Playing);

    // -----------------------------------------------------------------
    // The engine
    // -----------------------------------------------------------------

    public async Task<CricketResult<CricketMatch>> RecordBallAsync(int matchId, RecordBallRequest request)
    {
        var match = await LoadAsync(matchId);
        if (match == null) return CricketResult<CricketMatch>.Missing("Match not found.");

        var innings = CurrentInnings(match);
        if (innings == null || innings.Status != InningsStatus.InProgress)
            return CricketResult<CricketMatch>.Fail("No innings is under way.");

        if (innings.NeedsBatter)
            return CricketResult<CricketMatch>.Fail("A new batter must come in first.");

        if (innings.NeedsBowler)
            return CricketResult<CricketMatch>.Fail("The next bowler must be nominated first.");

        if (ValidateBall(match, innings, request) is { } error)
            return CricketResult<CricketMatch>.Fail(error);

        var legalSoFar = innings.LegalBalls;
        var ball = new CricketBall
        {
            InningsId = innings.Id,
            SequenceNumber = innings.Balls.Count + 1,
            OverNumber = legalSoFar / innings.BallsPerOver + 1,
            BallInOver = legalSoFar % innings.BallsPerOver + 1,
            BowlerId = innings.CurrentBowlerId!.Value,
            StrikerId = innings.StrikerId!.Value,
            NonStrikerId = innings.NonStrikerId,
            RunsOffBat = request.RunsOffBat,
            IsWide = request.IsWide,
            IsNoBall = request.IsNoBall,
            WideExtraRuns = request.IsWide ? request.WideExtraRuns : 0,
            Byes = request.Byes,
            LegByes = request.LegByes,
            PenaltyRuns = request.PenaltyRuns,
            WidePenalty = match.Rules.WidePenaltyRuns,
            NoBallPenalty = match.Rules.NoBallPenaltyRuns,
            IsFreeHit = innings.FreeHitPending,
            WicketType = request.WicketType,
            DismissedPlayerId = request.WicketType.HasValue
                ? request.DismissedPlayerId ?? innings.StrikerId
                : null,
            FielderId = request.WicketType.HasValue ? request.FielderId : null,
            BattersCrossed = request.BattersCrossed
        };

        innings.Balls.Add(ball);
        Recompute(innings, match.Rules);
        CloseInningsIfFinished(match, innings);

        await _db.SaveChangesAsync();
        return CricketResult<CricketMatch>.Success(match);
    }

    /// <summary>
    /// Everything the laws — and this league's variations on them — forbid. Each check exists
    /// because getting it wrong produces a scorecard that looks plausible and is wrong.
    /// </summary>
    private static string? ValidateBall(CricketMatch match, CricketInnings innings, RecordBallRequest request)
    {
        var rules = match.Rules;

        if (request.IsWide && request.IsNoBall)
            return "A delivery is either a wide or a no-ball, not both.";

        if (request.RunsOffBat < 0 || request.Byes < 0 || request.LegByes < 0
            || request.PenaltyRuns < 0 || request.WideExtraRuns < 0)
            return "Runs cannot be negative.";

        if (request.IsWide && request.RunsOffBat > 0)
            return "A wide cannot be hit, so nothing is scored off the bat.";

        if (request.IsWide && (request.Byes > 0 || request.LegByes > 0))
            return "Runs run off a wide are scored as wides, not byes.";

        if (request.Byes > 0 && !rules.ByesAllowed)
            return "Byes are not played in this match.";

        if (request.LegByes > 0 && !rules.LegByesAllowed)
            return "Leg byes are not played in this match.";

        if (request.PenaltyRuns > 0 && !rules.PenaltyRunsAllowed)
            return "Penalty runs are not played in this match.";

        if (request.Byes > 0 && request.LegByes > 0)
            return "A delivery is either byes or leg byes, not both.";

        if (request.WicketType is not { } wicket) return null;

        if (wicket == DismissalType.LBW && !rules.LbwEnabled)
            return "LBW is not played in this match.";

        if (request.IsWide && !DismissalRules.PossibleOffWide(wicket))
            return $"{wicket} is not possible off a wide.";

        if (innings.FreeHitPending && !DismissalRules.PossibleOnFreeHit(wicket))
            return $"{wicket} is not possible on a free hit.";

        var dismissed = request.DismissedPlayerId ?? innings.StrikerId;
        if (dismissed != innings.StrikerId && dismissed != innings.NonStrikerId)
            return "The dismissed player is not at the crease.";

        if (wicket == DismissalType.RunOut && request.DismissedPlayerId is null)
            return "A run-out must say which batter was dismissed.";

        if (request.FielderId is int fielder && !IsPlaying(match, fielder, innings.BowlingTeamId))
            return "The fielder is not in the fielding side's XI.";

        if (DismissalRules.TakesFielder(wicket) && request.FielderId is null
            && wicket != DismissalType.RunOut)
            return $"{wicket} needs the fielder who took it.";

        return null;
    }

    /// <summary>
    /// Re-folds the innings from its deliveries: totals, who is on strike, whose over it is, and
    /// whether a free hit is live. Nothing is ever adjusted in place, so this is equally the
    /// "apply a ball" path and the "undo a ball" path, and the two cannot disagree.
    /// </summary>
    private static void Recompute(CricketInnings innings, CricketMatchRules rules)
    {
        innings.Runs = innings.Wickets = innings.LegalBalls = 0;
        innings.Wides = innings.NoBalls = innings.Byes = innings.LegByes = innings.PenaltyRuns = 0;

        // An empty ledger folds back to the openers, which is why they are kept.
        int? striker = innings.OpeningStrikerId;
        int? nonStriker = innings.OpeningNonStrikerId;
        int? currentBowler = innings.OpeningBowlerId;
        int? previousBowler = null;
        var freeHit = false;
        var lone = false;

        // Who has been to the crease. Once that is the whole side, a last-man-standing innings has
        // nobody left to send out and the survivor bats on alone.
        var appeared = new HashSet<int>();
        if (innings.OpeningStrikerId is int o1) appeared.Add(o1);
        if (innings.OpeningNonStrikerId is int o2) appeared.Add(o2);

        foreach (var ball in innings.Balls.OrderBy(b => b.SequenceNumber))
        {
            appeared.Add(ball.StrikerId);
            if (ball.NonStrikerId is int partner) appeared.Add(partner);

            innings.Runs += ball.TotalRuns;
            innings.Wides += ball.WideRuns;
            innings.NoBalls += ball.NoBallRuns;
            innings.Byes += ball.Byes;
            innings.LegByes += ball.LegByes;
            innings.PenaltyRuns += ball.PenaltyRuns;

            if (ball.IsLegalDelivery) innings.LegalBalls++;
            if (ball.CountsAsWicket) innings.Wickets++;

            striker = ball.StrikerId;
            nonStriker = ball.NonStrikerId;

            // Odd running changes ends. A catch scores nothing, so whether the batters crossed is
            // the only thing that decides which end the incoming batter walks to.
            var swap = ball.RunsCompleted % 2 == 1;
            if (ball.WicketType == DismissalType.Caught && ball.BattersCrossed) swap = !swap;
            if (swap && !lone) (striker, nonStriker) = (nonStriker, striker);

            // Whoever went leaves their end empty — including a retirement, which is not a wicket
            // but still needs replacing.
            if (ball.DismissedPlayerId is int out_)
            {
                if (striker == out_) striker = null;
                else if (nonStriker == out_) nonStriker = null;
            }

            // The whole side has now batted and one is left: from here they face every ball.
            if (!lone && rules.LastManStanding
                      && appeared.Count >= innings.BattingSideSize
                      && (striker is null) != (nonStriker is null))
            {
                lone = true;
                striker ??= nonStriker;
                nonStriker = null;
            }

            currentBowler = ball.BowlerId;

            if (ball.IsLegalDelivery && innings.LegalBalls % innings.BallsPerOver == 0)
            {
                if (!lone) (striker, nonStriker) = (nonStriker, striker);
                previousBowler = ball.BowlerId;
                currentBowler = null;
            }

            // Neither a wide nor a no-ball consumes a free hit; only a legal delivery does.
            freeHit = ball.IsNoBall ? rules.FreeHitAfterNoBall || freeHit
                : ball.IsWide ? rules.FreeHitAfterWide || freeHit
                : false;
        }

        innings.StrikerId = striker;
        innings.NonStrikerId = nonStriker;
        innings.CurrentBowlerId = currentBowler;
        innings.PreviousBowlerId = previousBowler;
        innings.FreeHitPending = freeHit;
        innings.LoneBatter = lone;
    }

    public async Task<CricketResult<CricketMatch>> UndoLastBallAsync(int matchId)
    {
        var match = await LoadAsync(matchId);
        if (match == null) return CricketResult<CricketMatch>.Missing("Match not found.");

        var innings = CurrentInnings(match);
        if (innings == null) return CricketResult<CricketMatch>.Fail("No innings to undo.");

        var last = innings.Balls.OrderByDescending(b => b.SequenceNumber).FirstOrDefault();
        if (last == null) return CricketResult<CricketMatch>.Fail("No deliveries have been bowled in this innings.");

        innings.Balls.Remove(last);
        _db.CricketBalls.Remove(last);

        // The ball may have been the one that ended the innings, and possibly the match with it.
        innings.Status = InningsStatus.InProgress;
        innings.EndReason = null;
        innings.CompletedAt = null;

        if (match.Status is CricketMatchStatus.Completed or CricketMatchStatus.InningsBreak)
        {
            match.Status = innings.IsSuperOver ? CricketMatchStatus.SuperOver : CricketMatchStatus.InProgress;
            match.WinnerTeamId = null;
            match.IsTie = match.IsDraw = match.IsNoResult = false;
            match.ResultSummary = null;
            match.CompletedAt = null;
        }

        Recompute(innings, match.Rules);
        CloseInningsIfFinished(match, innings);

        Log(match, CricketMatchEventType.BallUndone, "The last delivery was undone.");

        await _db.SaveChangesAsync();
        return CricketResult<CricketMatch>.Success(match);
    }

    // -----------------------------------------------------------------
    // Batters and bowlers
    // -----------------------------------------------------------------

    public async Task<CricketResult<CricketMatch>> SetBatterAsync(int matchId, NewBatterRequest request)
    {
        var match = await LoadAsync(matchId);
        if (match == null) return CricketResult<CricketMatch>.Missing("Match not found.");

        var innings = CurrentInnings(match);
        if (innings == null || innings.Status != InningsStatus.InProgress)
            return CricketResult<CricketMatch>.Fail("No innings is under way.");

        if (!innings.NeedsBatter)
            return CricketResult<CricketMatch>.Fail("Both batters are already at the crease.");

        if (!IsPlaying(match, request.PlayerId, innings.BattingTeamId))
            return CricketResult<CricketMatch>.Fail("That player is not in the batting side's XI.");

        if (request.PlayerId == innings.StrikerId || request.PlayerId == innings.NonStrikerId)
            return CricketResult<CricketMatch>.Fail("That batter is already at the crease.");

        if (IsOut(innings, request.PlayerId))
            return CricketResult<CricketMatch>.Fail("That batter is already out.");

        if (innings.StrikerId == null) innings.StrikerId = request.PlayerId;
        else innings.NonStrikerId = request.PlayerId;

        Log(match, CricketMatchEventType.BatterIn,
            $"{NameOf(match, request.PlayerId)} came in.", innings.BattingTeamId, request.PlayerId);

        await _db.SaveChangesAsync();
        return CricketResult<CricketMatch>.Success(match);
    }

    /// <summary>
    /// Out for this innings. Retiring hurt is pointedly not out — the batter may come back — which
    /// is why this asks <see cref="DismissalRules.CountsAsWicket"/> rather than just "was dismissed".
    /// </summary>
    private static bool IsOut(CricketInnings innings, int playerId) =>
        innings.Balls.Any(b => b.DismissedPlayerId == playerId && b.CountsAsWicket);

    public async Task<CricketResult<CricketMatch>> SetBowlerAsync(int matchId, NewBowlerRequest request)
    {
        var match = await LoadAsync(matchId);
        if (match == null) return CricketResult<CricketMatch>.Missing("Match not found.");

        var innings = CurrentInnings(match);
        if (innings == null || innings.Status != InningsStatus.InProgress)
            return CricketResult<CricketMatch>.Fail("No innings is under way.");

        if (!innings.NeedsBowler)
            return CricketResult<CricketMatch>.Fail("This over already has a bowler.");

        if (!IsPlaying(match, request.PlayerId, innings.BowlingTeamId))
            return CricketResult<CricketMatch>.Fail("That player is not in the fielding side's XI.");

        if (request.PlayerId == innings.PreviousBowlerId)
            return CricketResult<CricketMatch>.Fail("A bowler may not bowl two overs in a row.");

        if (innings.MaxOversPerBowler is int max)
        {
            var bowled = CompletedOversBy(innings, request.PlayerId);
            if (bowled >= max)
                return CricketResult<CricketMatch>.Fail(
                    $"{NameOf(match, request.PlayerId)} has bowled the maximum of {max} overs.");
        }

        innings.CurrentBowlerId = request.PlayerId;

        Log(match, CricketMatchEventType.BowlerChanged,
            $"{NameOf(match, request.PlayerId)} came on to bowl.", innings.BowlingTeamId, request.PlayerId);

        await _db.SaveChangesAsync();
        return CricketResult<CricketMatch>.Success(match);
    }

    private static int CompletedOversBy(CricketInnings innings, int playerId)
    {
        var legal = innings.Balls.Count(b => b.BowlerId == playerId && b.IsLegalDelivery);
        return legal / innings.BallsPerOver;
    }

    private static string NameOf(CricketMatch match, int playerId) =>
        match.Squad.FirstOrDefault(p => p.PlayerId == playerId)?.PlayerName ?? $"Player {playerId}";
}
