using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentScheduler.Api.Data;
using TournamentScheduler.Api.Models;

namespace TournamentScheduler.Api.Controllers;

[ApiController]
[Route("api/matches")]
public class MatchesController : ControllerBase
{
    private readonly TournamentDbContext _db;

    public MatchesController(TournamentDbContext db)
    {
        _db = db;
    }

    // GET api/matches/5
    [HttpGet("{id}")]
    public async Task<ActionResult<object>> GetMatch(int id)
    {
        var match = await _db.Matches
            .Include(m => m.HomeTeam).ThenInclude(t => t!.Players)
            .Include(m => m.AwayTeam).ThenInclude(t => t!.Players)
            .Include(m => m.MatchPlayers)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (match == null) return NotFound();
        return Ok(match);
    }

    // POST api/matches/5/setup — configures the match and immediately starts it
    [HttpPost("{id}/setup")]
    public async Task<ActionResult<object>> SetupAndStart(int id, [FromBody] StartMatchRequest request)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound("Match not found.");

        if (match.Status != MatchStatus.NotStarted)
            return BadRequest("This match has already been set up or started.");
        // Enforce: only one match in progress per tournament at a time
        var conflicting = await _db.Matches.AnyAsync(m =>
            m.TournamentId == match.TournamentId &&
            m.Id != id &&
            (m.Status == MatchStatus.InProgress || m.Status == MatchStatus.Paused));
        if (conflicting)
            return Conflict("Another match in this tournament is already in progress. Finish or complete it before starting a new one.");
        if (request.DrawAllowed && request.ExtraTimeAllowed)
            return BadRequest("Draw allowed and extra time allowed can't both be enabled for the same match.");
        if (request.MaxPlayersPerSide < 1)
            return BadRequest("Max players per side must be at least 1.");
        if (request.MinutesPerHalf < 1)
            return BadRequest("Minutes per half must be at least 1.");
        if (request.ExtraTimeAllowed)
        {
            if (!request.ExtraTimeMinutesPerHalf.HasValue || request.ExtraTimeMinutesPerHalf < 1)
                return BadRequest("Extra time half length is required and must be at least 1 minute when extra time is allowed.");

            if (request.ExtraTimeMinutesPerHalf > request.MinutesPerHalf)
                return BadRequest($"Extra time half length ({request.ExtraTimeMinutesPerHalf} min) can't exceed the regulation half length ({request.MinutesPerHalf} min).");
        }


        var homeStarting = request.Squad.Count(s => s.TeamId == match.HomeTeamId && s.SquadStatus == SquadStatus.Starting);
        var awayStarting = request.Squad.Count(s => s.TeamId == match.AwayTeamId && s.SquadStatus == SquadStatus.Starting);
        if (homeStarting != request.MaxPlayersPerSide || awayStarting != request.MaxPlayersPerSide)
            return BadRequest($"Each team must have exactly {request.MaxPlayersPerSide} starting players selected.");

        match.MaxPlayersPerSide = request.MaxPlayersPerSide;
        match.MinutesPerHalf = request.MinutesPerHalf;
        match.ExtraTimeAllowed = request.ExtraTimeAllowed;
        match.DrawAllowed = request.DrawAllowed;
        match.MaxSubstitutions = request.MaxSubstitutions;
        match.RollingSubsAllowed = request.RollingSubsAllowed;
        match.ExtraTimeMinutesPerHalf = request.ExtraTimeAllowed ? request.ExtraTimeMinutesPerHalf : null;
        match.MatchPlayers = request.Squad.Select(s => new MatchPlayer
        {
            PlayerId = s.PlayerId,
            TeamId = s.TeamId,
            SquadStatus = s.SquadStatus
        }).ToList();

        match.Status = MatchStatus.InProgress;
        match.StartedAt = DateTime.UtcNow;
        match.CurrentMinute = 0;
        match.CurrentHalf = 1;
        match.IsClockRunning = true;
        match.HalfStartedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { matchId = match.Id, status = match.Status.ToString() });
    }

    // POST api/matches/5/events — register a goal, card, or substitution
    [HttpPost("{id}/events")]
    public async Task<ActionResult<object>> RecordEvent(int id, [FromBody] RecordEventRequest request)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound("Match not found.");

        if (match.Status != MatchStatus.InProgress && match.Status != MatchStatus.Paused)
            return BadRequest("This match isn't currently active.");        

        if (request.EventType == MatchEventType.RedCard)
        {
            if (request.PlayerId == null)
                return BadRequest("A red card requires a player.");
            if (request.TeamId == null)
                return BadRequest("A red card requires a team.");

            var sentOff = await _db.MatchPlayers.FirstOrDefaultAsync(mp => mp.MatchId == id && mp.PlayerId == request.PlayerId);
            if (sentOff == null) return BadRequest("Player isn't part of this match's squad.");
            if (sentOff.SquadStatus != SquadStatus.Starting) return BadRequest("Only a player currently on the pitch can be sent off.");

            sentOff.SquadStatus = SquadStatus.SentOff;

            // Record the red card itself in the timeline before checking for a forfeit,
            // so the event that caused the abandonment is still visible in the match review.
            var (redBaseMinute, redStoppageMinute) = CalculateMinuteBreakdown(match);
            _db.MatchEvents.Add(new MatchEvent
            {
                MatchId = id,
                EventType = MatchEventType.RedCard,
                MinuteOfMatch = redBaseMinute,
                StoppageMinute = redStoppageMinute,
                TeamId = request.TeamId,
                PlayerId = request.PlayerId
            });

            var abandoned = await CheckAndHandleTeamDepleted(match, request.TeamId.Value);
            if (abandoned)
            {
                await _db.SaveChangesAsync();
                return Ok(new { eventId = (int?)null, match.HomeScore, match.AwayScore, matchAbandoned = true });
            }
            await _db.SaveChangesAsync();
            return Ok(new { eventId = (int?)null, match.HomeScore, match.AwayScore });
        }
        if (request.EventType == MatchEventType.YellowCard)
        {
            if (request.PlayerId == null)
                return BadRequest("A yellow card requires a player.");
            if (request.TeamId == null)
                return BadRequest("A yellow card requires a team.");

            var player = await _db.MatchPlayers.FirstOrDefaultAsync(mp => mp.MatchId == id && mp.PlayerId == request.PlayerId);
            if (player == null) return BadRequest("Player isn't part of this match's squad.");
            if (player.SquadStatus == SquadStatus.SentOff) return BadRequest("This player has already been sent off.");

            var priorYellows = await _db.MatchEvents.CountAsync(e =>
                e.MatchId == id && e.EventType == MatchEventType.YellowCard && e.PlayerId == request.PlayerId);

            if (priorYellows >= 1)
            {
                // Second yellow — automatic send-off, matching real football law.
                player.SquadStatus = SquadStatus.SentOff;

                // Record the yellow card itself before checking for a forfeit, same reasoning as above.
                var (yellowBaseMinute, yellowStoppageMinute) = CalculateMinuteBreakdown(match);
                _db.MatchEvents.Add(new MatchEvent
                {
                    MatchId = id,
                    EventType = MatchEventType.YellowCard,
                    MinuteOfMatch = yellowBaseMinute,
                    StoppageMinute = yellowStoppageMinute,
                    TeamId = request.TeamId,
                    PlayerId = request.PlayerId
                });

                var abandoned = await CheckAndHandleTeamDepleted(match, request.TeamId.Value);
                if (abandoned)
                {
                    await _db.SaveChangesAsync();
                    return Ok(new { eventId = (int?)null, match.HomeScore, match.AwayScore, matchAbandoned = true });
                }
                await _db.SaveChangesAsync();
                return Ok(new { eventId = (int?)null, match.HomeScore, match.AwayScore });
            }
        }
        if (request.EventType == MatchEventType.SubstitutionIn)
        {
            if (request.PlayerId == null || request.RelatedPlayerId == null)
                return BadRequest("Substitution requires both an outgoing and an incoming player.");
            if (request.TeamId == null)
                return BadRequest("Substitution requires a team.");

            var subsUsedByTeam = await _db.MatchEvents.CountAsync(e =>
                    e.MatchId == id && e.EventType == MatchEventType.SubstitutionIn && e.TeamId == request.TeamId);
            
            if (match.MaxSubstitutions.HasValue && subsUsedByTeam >= match.MaxSubstitutions.Value)
            {
                var teamName = request.TeamId == match.HomeTeamId ? match.HomeTeamName : match.AwayTeamName;
                return BadRequest($"Maximum substitutions ({match.MaxSubstitutions}) already used for this match.");
            }
            
            var outgoing = await _db.MatchPlayers.FirstOrDefaultAsync(mp => mp.MatchId == id && mp.PlayerId == request.PlayerId);
            var incoming = await _db.MatchPlayers.FirstOrDefaultAsync(mp => mp.MatchId == id && mp.PlayerId == request.RelatedPlayerId);

            if (outgoing == null || incoming == null) return BadRequest("Both players must be part of this match's squad.");
            if (outgoing.SquadStatus != SquadStatus.Starting) return BadRequest("The outgoing player isn't currently on the pitch.");
            if (incoming.SquadStatus != SquadStatus.Bench) return BadRequest("The incoming player isn't available on the bench.");

            outgoing.SquadStatus = match.RollingSubsAllowed ? SquadStatus.Bench : SquadStatus.SubstitutedOff;
            incoming.SquadStatus = SquadStatus.Starting;
        }

        var (baseMinute, stoppageMinute) = CalculateMinuteBreakdown(match);
        var evt = new MatchEvent
        {
            MatchId = id,
            EventType = request.EventType,
            MinuteOfMatch = baseMinute,
            StoppageMinute = stoppageMinute,
            TeamId = request.TeamId,
            PlayerId = request.PlayerId,
            RelatedPlayerId = request.RelatedPlayerId
        };
        _db.MatchEvents.Add(evt);

        if (request.EventType == MatchEventType.Goal)
        {
            if (request.TeamId == match.HomeTeamId) match.HomeScore++;
            else if (request.TeamId == match.AwayTeamId) match.AwayScore++;
        }

        await _db.SaveChangesAsync();
        return Ok(new { eventId = evt.Id, match.HomeScore, match.AwayScore });
    }

    // GET api/matches/5/events — full timeline, oldest first
    [HttpGet("{id}/events")]
    public async Task<ActionResult<object>> GetEvents(int id)
    {
        var events = await _db.MatchEvents
            .Where(e => e.MatchId == id)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new
            {
                e.Id,
                EventType = e.EventType.ToString(),
                e.MinuteOfMatch,
                e.StoppageMinute,
                e.TeamId,
                e.PlayerId,
                e.RelatedPlayerId,
                e.PenaltyScored,
                e.PenaltyHomeScoreAfter,
                e.PenaltyAwayScoreAfter,
                e.CreatedAt
            })
            .ToListAsync();

        return Ok(events);
    }

    // POST api/matches/5/clock/pause
    [HttpPost("{id}/clock/pause")]
    public async Task<ActionResult<object>> PauseClock(int id)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound();
        if (match.Status != MatchStatus.InProgress) return BadRequest("Match isn't in progress.");

        match.Status = MatchStatus.Paused;
        match.IsClockRunning = false;
        match.PausedAt = DateTime.UtcNow;
        var (baseMinute, stoppageMinute) = CalculateMinuteBreakdown(match);
        _db.MatchEvents.Add(new MatchEvent { MatchId = id, EventType = MatchEventType.ClockPaused, MinuteOfMatch = baseMinute, StoppageMinute = stoppageMinute });
        await _db.SaveChangesAsync();
        return Ok(new { match.Status, minuteOfMatch = CalculateMinuteBreakdown(match) });
    }

    // POST api/matches/5/clock/resume
    [HttpPost("{id}/clock/resume")]
    public async Task<ActionResult<object>> ResumeClock(int id)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound();
        if (match.Status != MatchStatus.Paused) return BadRequest("Match isn't paused.");

        if (match.PausedAt.HasValue)
        {
            match.PausedDurationMs += (long)(DateTime.UtcNow - match.PausedAt.Value).TotalMilliseconds;
            match.PausedAt = null;
        }

        match.Status = MatchStatus.InProgress;
        match.IsClockRunning = true;
        var (baseMinute, stoppageMinute) = CalculateMinuteBreakdown(match);
        _db.MatchEvents.Add(new MatchEvent { MatchId = id, EventType = MatchEventType.ClockResumed, MinuteOfMatch = baseMinute, StoppageMinute = stoppageMinute });
        await _db.SaveChangesAsync();
        return Ok(new { match.Status, minuteOfMatch = CalculateMinuteBreakdown(match) });
    }

    // POST api/matches/5/clock/add-time
    [HttpPost("{id}/clock/add-time")]
    public async Task<ActionResult<object>> AddTime(int id, [FromBody] AddTimeRequest request)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound();
        if (request.Minutes < 1) return BadRequest("Minutes must be at least 1.");

        var halfLength = GetHalfLengthMinutes(match, match.CurrentHalf);
        var maxAllowed = (int)Math.Ceiling(halfLength * 0.5);
        var remaining = maxAllowed - match.ExtraMinutesAddedThisHalf;

        if (remaining <= 0)
            return BadRequest($"Maximum stoppage time for this half ({maxAllowed} min) has already been added.");

        if (request.Minutes > remaining)
            return BadRequest($"You can add at most {remaining} more minute(s) this half (cap is 50% of the half length: {maxAllowed} min).");

        match.ExtraMinutesAddedThisHalf += request.Minutes;
        var (baseMinute, stoppageMinute) = CalculateMinuteBreakdown(match);
        _db.MatchEvents.Add(new MatchEvent { MatchId = id, EventType = MatchEventType.ExtraTimeAdded, MinuteOfMatch = baseMinute, StoppageMinute = stoppageMinute });
        await _db.SaveChangesAsync();

        return Ok(new
        {
            addedMinutes = request.Minutes,
            minuteOfMatch = CalculateMinuteBreakdown(match),
            remainingAllowance = maxAllowed - match.ExtraMinutesAddedThisHalf,
            maxAllowedThisHalf = maxAllowed
        });
    }

    // POST api/matches/5/half/next — ends current half, starts the next
    [HttpPost("{id}/half/next")]
    public async Task<ActionResult<object>> NextHalf(int id)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound();

        // Cap halves: normal match = 2, with extra time allowed = up to 4 (regulation + 2 ET halves)
        int maxHalves;
        if (match.CurrentHalf <= 2)
        {
            bool scoresLevel = match.HomeScore == match.AwayScore;
            bool extraTimeApplicable = match.ExtraTimeAllowed && !match.DrawAllowed && scoresLevel;
            maxHalves = extraTimeApplicable ? 4 : 2;
        }
        else
        {
            maxHalves = 4; // already in extra time, allow up to ET2
        }
        if (match.CurrentHalf >= maxHalves)
            return BadRequest($"This match already has the maximum number of halves ({maxHalves}). End the match instead.");

        var (baseMinute, stoppageMinute) = CalculateMinuteBreakdown(match);
        _db.MatchEvents.Add(new MatchEvent { MatchId = id, EventType = MatchEventType.HalfEnd, MinuteOfMatch = baseMinute, StoppageMinute = stoppageMinute });

        match.CurrentHalf++;
        match.Status = MatchStatus.Paused;
        match.IsClockRunning = false;
        match.PausedAt = DateTime.UtcNow;      // clock paused until Resume kicks off the new half
        match.PausedDurationMs = 0;            // fresh pause tracking for the new half
        match.HalfStartedAt = DateTime.UtcNow; // fresh elapsed-time baseline for the new half
        match.ExtraMinutesAddedThisHalf = 0;   // fresh stoppage allowance for the new half

        _db.MatchEvents.Add(new MatchEvent { MatchId = id, EventType = MatchEventType.HalfStart, MinuteOfMatch = baseMinute, StoppageMinute = stoppageMinute });

        await _db.SaveChangesAsync();
        return Ok(new { match.CurrentHalf, match.Status, maxHalves });
    }

    // POST api/matches/5/complete
    [HttpPost("{id}/complete")]
    public async Task<ActionResult<object>> CompleteMatch(int id)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound();

        match.Status = MatchStatus.Completed;
        match.CompletedAt = DateTime.UtcNow;
        match.IsClockRunning = false;
        var (baseMinute, stoppageMinute) = CalculateMinuteBreakdown(match);
        _db.MatchEvents.Add(new MatchEvent { MatchId = id, EventType = MatchEventType.MatchCompleted, MinuteOfMatch = baseMinute, StoppageMinute = stoppageMinute });

        await _db.SaveChangesAsync();
        return Ok(new { match.Status, match.HomeScore, match.AwayScore });
    }

    // POST api/matches/5/penalties/start
    [HttpPost("{id}/penalties/start")]
    public async Task<ActionResult<object>> StartPenalties(int id, [FromBody] StartPenaltiesRequest request)
    {
        var match = await _db.Matches.Include(m => m.MatchPlayers).FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound();
        if (match.Status != MatchStatus.Paused) return BadRequest("Match must be paused at the end of the final half to start penalties.");
        if (match.HomeScore != match.AwayScore) return BadRequest("Penalties can only start when scores are level.");
        if (match.DrawAllowed) return BadRequest("This match allows draws — penalties aren't applicable.");
        if (request.TakersPerSide < 1) return BadRequest("Takers per side must be at least 1.");

        var (baseMinute, _) = CalculateMinuteBreakdown(match);
        match.FinalWhistleMinute = baseMinute;

        match.IsPenaltyShootout = true;
        match.PenaltyTakersPerSide = request.TakersPerSide;
        match.Status = MatchStatus.PenaltyShootout;

        _db.MatchEvents.Add(new MatchEvent { MatchId = id, EventType = MatchEventType.PenaltyShootoutStarted, MinuteOfMatch = baseMinute });

        await _db.SaveChangesAsync();
        return Ok(new { match.Status, match.PenaltyTakersPerSide });
    }

    // GET api/matches/5/penalties
    [HttpGet("{id}/penalties")]
    public async Task<ActionResult<object>> GetPenalties(int id)
    {
        var match = await _db.Matches.Include(m => m.MatchPlayers).FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound();

        var kicks = await _db.PenaltyKicks.Where(k => k.MatchId == id).OrderBy(k => k.CreatedAt).ToListAsync();
        var status = ComputeShootoutStatus(match, kicks);

        return Ok(new
        {
            kicks = kicks.Select(k => new { k.Id, k.TeamId, k.PlayerId, k.RoundNumber, k.IsSuddenDeath, k.Scored }),
            match.PenaltyTakersPerSide,
            match.PenaltyWinnerTeamId,
            outcome = status.Outcome,
            homeScore = status.HomeScored,
            awayScore = status.AwayScored,
            homeAvailableTakerIds = GetAvailableTakerIds(match, match.HomeTeamId ?? 0, kicks),
            awayAvailableTakerIds = GetAvailableTakerIds(match, match.AwayTeamId ?? 0, kicks)
        });
    }

    // POST api/matches/5/penalties/kick
    [HttpPost("{id}/penalties/kick")]
    public async Task<ActionResult<object>> RecordPenaltyKick(int id, [FromBody] RecordPenaltyKickRequest request)
    {
        var match = await _db.Matches.Include(m => m.MatchPlayers).FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound();
        if (match.Status != MatchStatus.PenaltyShootout) return BadRequest("Penalty shootout isn't active for this match.");
        if (request.PlayerId == null) return BadRequest("A penalty kick requires a taker.");

        var eligible = GetEligiblePlayerIds(match, request.TeamId);
        if (!eligible.Contains(request.PlayerId.Value))
            return BadRequest("Only players currently on the pitch can take penalty kicks.");

        var existingKicks = await _db.PenaltyKicks.Where(k => k.MatchId == id).ToListAsync();
        var availableTakers = GetAvailableTakerIds(match, request.TeamId, existingKicks);
        if (!availableTakers.Contains(request.PlayerId.Value))
            return BadRequest("This player has already taken a kick — every eligible player must kick once before anyone repeats.");

        var takers = match.PenaltyTakersPerSide ?? 5;
        var teamRegularCount = existingKicks.Count(k => k.TeamId == request.TeamId && !k.IsSuddenDeath);
        var isSuddenDeath = teamRegularCount >= takers;
        var roundNumber = isSuddenDeath
            ? existingKicks.Count(k => k.TeamId == request.TeamId && k.IsSuddenDeath) + 1
            : teamRegularCount + 1;

        var kick = new PenaltyKick
        {
            MatchId = id,
            TeamId = request.TeamId,
            PlayerId = request.PlayerId,
            RoundNumber = roundNumber,
            IsSuddenDeath = isSuddenDeath,
            Scored = request.Scored
        };
        _db.PenaltyKicks.Add(kick);
        existingKicks.Add(kick);

        var status = ComputeShootoutStatus(match, existingKicks);

        _db.MatchEvents.Add(new MatchEvent
        {
            MatchId = id,
            EventType = MatchEventType.PenaltyKick,
            MinuteOfMatch = match.FinalWhistleMinute ?? 0,
            TeamId = request.TeamId,
            PlayerId = request.PlayerId,
            PenaltyScored = request.Scored,
            PenaltyHomeScoreAfter = status.HomeScored,
            PenaltyAwayScoreAfter = status.AwayScored
        });

        if (status.Outcome != "InProgress")
        {
            match.PenaltyWinnerTeamId = status.Outcome == "HomeWins" ? match.HomeTeamId : match.AwayTeamId;
            match.PenaltyHomeScore = status.HomeScored;
            match.PenaltyAwayScore = status.AwayScored;
            match.Status = MatchStatus.Completed;
            match.CompletedAt = DateTime.UtcNow;
            match.IsClockRunning = false;
            _db.MatchEvents.Add(new MatchEvent { MatchId = id, EventType = MatchEventType.MatchCompleted, MinuteOfMatch = match.FinalWhistleMinute ?? 0 });
        }

        await _db.SaveChangesAsync();
        return Ok(new { kickId = kick.Id, outcome = status.Outcome, homeScore = status.HomeScored, awayScore = status.AwayScored, match.Status });
    }

    // POST api/matches/5/penalties/end — manual override, declare a winner directly
    [HttpPost("{id}/penalties/end")]
    public async Task<ActionResult<object>> EndPenaltiesManually(int id, [FromBody] EndPenaltiesRequest request)
    {
        var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound();
        if (match.Status != MatchStatus.PenaltyShootout) return BadRequest("Penalty shootout isn't active for this match.");
        if (request.WinningTeamId != match.HomeTeamId && request.WinningTeamId != match.AwayTeamId)
            return BadRequest("Winning team must be one of the two teams in this match.");

        match.PenaltyWinnerTeamId = request.WinningTeamId;
        match.Status = MatchStatus.Completed;
        match.CompletedAt = DateTime.UtcNow;
        match.IsClockRunning = false;

        var kicks = await _db.PenaltyKicks.Where(k => k.MatchId == id).ToListAsync();
        var status = ComputeShootoutStatus(match, kicks);
        match.PenaltyHomeScore = status.HomeScored;
        match.PenaltyAwayScore = status.AwayScored;
        match.PenaltyWinnerTeamId = request.WinningTeamId;
        match.Status = MatchStatus.Completed;

        _db.MatchEvents.Add(new MatchEvent { MatchId = id, EventType = MatchEventType.MatchCompleted, MinuteOfMatch = match.FinalWhistleMinute ?? 0 });

        await _db.SaveChangesAsync();
        return Ok(new { match.Status, match.PenaltyWinnerTeamId });
    }

    private static (string Outcome, int HomeScored, int AwayScored) ComputeShootoutStatus(Match match, List<PenaltyKick> kicks)
    {
        var takers = match.PenaltyTakersPerSide ?? 5;
        var homeId = match.HomeTeamId ?? 0;
        var awayId = match.AwayTeamId ?? 0;

        var homeRegular = kicks.Where(k => k.TeamId == homeId && !k.IsSuddenDeath).ToList();
        var awayRegular = kicks.Where(k => k.TeamId == awayId && !k.IsSuddenDeath).ToList();
        var homeScored = homeRegular.Count(k => k.Scored);
        var awayScored = awayRegular.Count(k => k.Scored);
        var homeRemaining = Math.Max(0, takers - homeRegular.Count);
        var awayRemaining = Math.Max(0, takers - awayRegular.Count);

        if (homeScored > awayScored + awayRemaining) return ("HomeWins", homeScored, awayScored);
        if (awayScored > homeScored + homeRemaining) return ("AwayWins", homeScored, awayScored);
        if (homeRegular.Count < takers || awayRegular.Count < takers) return ("InProgress", homeScored, awayScored);
        if (homeScored != awayScored) return (homeScored > awayScored ? "HomeWins" : "AwayWins", homeScored, awayScored);

        var homeSD = kicks.Where(k => k.TeamId == homeId && k.IsSuddenDeath).OrderBy(k => k.RoundNumber).ToList();
        var awaySD = kicks.Where(k => k.TeamId == awayId && k.IsSuddenDeath).OrderBy(k => k.RoundNumber).ToList();
        var totalHome = homeScored;
        var totalAway = awayScored;
        var rounds = Math.Min(homeSD.Count, awaySD.Count);

        for (var r = 0; r < rounds; r++)
        {
            if (homeSD[r].Scored) totalHome++;
            if (awaySD[r].Scored) totalAway++;
            if (totalHome != totalAway) return (totalHome > totalAway ? "HomeWins" : "AwayWins", totalHome, totalAway);
        }

        return ("InProgress", totalHome, totalAway);
    }

    private static int GetHalfLengthMinutes(Match match, int halfNumber)
    {
        if (halfNumber <= 2) return match.MinutesPerHalf ?? 0;
        return match.ExtraTimeMinutesPerHalf ?? match.MinutesPerHalf ?? 0;
    }

    private static int GetPriorHalvesMinutes(Match match)
    {
        var total = 0;
        for (var h = 1; h < match.CurrentHalf; h++)
            total += GetHalfLengthMinutes(match, h);
        return total;
    }
    private static (int BaseMinute, int? StoppageMinute) CalculateMinuteBreakdown(Match match)
    {
        if (match.HalfStartedAt == null) return (0, null);

        var pausedMs = match.PausedDurationMs;
        var activePauseMs = match.Status == MatchStatus.Paused && match.PausedAt.HasValue
            ? (DateTime.UtcNow - match.PausedAt.Value).TotalMilliseconds
            : 0;

        var elapsedMs = (DateTime.UtcNow - match.HalfStartedAt.Value).TotalMilliseconds - pausedMs - activePauseMs;
        var elapsedMinutesThisHalf = Math.Max(0, (int)(elapsedMs / 60000));

        var halfLength = GetHalfLengthMinutes(match, match.CurrentHalf);
        var priorHalvesMinutes = GetPriorHalvesMinutes(match);

        if (elapsedMinutesThisHalf <= halfLength)
            return (priorHalvesMinutes + elapsedMinutesThisHalf, null);

        var stoppage = elapsedMinutesThisHalf - halfLength;
        return (priorHalvesMinutes + halfLength, stoppage);
    }
    private static List<int> GetEligiblePlayerIds(Match match, int teamId)
    {
        return match.MatchPlayers
            .Where(mp => mp.TeamId == teamId && mp.SquadStatus == SquadStatus.Starting)
            .Select(mp => mp.PlayerId)
            .ToList();
    }
    private static List<int> GetAvailableTakerIds(Match match, int teamId, List<PenaltyKick> kicks)
    {
        var eligible = GetEligiblePlayerIds(match, teamId);
        if (eligible.Count == 0) return eligible;

        var teamKicks = kicks.Where(k => k.TeamId == teamId).OrderBy(k => k.CreatedAt).ToList();
        var usedThisCycle = new HashSet<int>();

        foreach (var k in teamKicks)
        {
            if (k.PlayerId.HasValue) usedThisCycle.Add(k.PlayerId.Value);
            if (usedThisCycle.Count >= eligible.Count)
                usedThisCycle.Clear(); // everyone eligible has kicked once — cycle resets, repeats now allowed
        }

        return eligible.Where(pid => !usedThisCycle.Contains(pid)).ToList();
    }
    private async Task<bool> CheckAndHandleTeamDepleted(Match match, int teamId)
    {
        var remainingOnPitch = await _db.MatchPlayers.CountAsync(mp =>
            mp.MatchId == match.Id && mp.TeamId == teamId && mp.SquadStatus == SquadStatus.Starting);

        if (remainingOnPitch > 0) return false;

        var opponentTeamId = teamId == match.HomeTeamId ? match.AwayTeamId : match.HomeTeamId;
        match.ForfeitWinnerTeamId = opponentTeamId;
        match.Status = MatchStatus.Completed;
        match.CompletedAt = DateTime.UtcNow;
        match.IsClockRunning = false;

        _db.MatchEvents.Add(new MatchEvent
        {
            MatchId = match.Id,
            EventType = MatchEventType.MatchAbandoned,
            MinuteOfMatch = CalculateMinuteBreakdown(match).BaseMinute,
            TeamId = opponentTeamId
        });

        return true;
    }
}