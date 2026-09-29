using TournamentScheduler.Api.Models.Cricket;

namespace TournamentScheduler.Api.Services.Cricket;

/// <summary>
/// The outcome of an engine call. Cricket rejects a great deal — a bowler on his limit, an LBW in
/// a league that does not play it, a catch off a free hit — and the caller needs to tell "no such
/// match" from "not allowed", so the reason travels with the result rather than as an exception.
/// </summary>
public class CricketResult<T>
{
    public T? Value { get; init; }
    public string? Error { get; init; }
    public bool NotFound { get; init; }

    public bool Ok => Error == null && !NotFound;

    public static CricketResult<T> Success(T value) => new() { Value = value };
    public static CricketResult<T> Fail(string error) => new() { Error = error };
    public static CricketResult<T> Missing(string error) => new() { Error = error, NotFound = true };
}

public interface ICricketScoringService
{
    Task<CricketMatch?> GetAsync(int matchId);

    Task<CricketResult<CricketMatch>> SetupAsync(int matchId, CricketSetupRequest request);
    Task<CricketResult<CricketMatch>> StartInningsAsync(int matchId, StartInningsRequest request);
    Task<CricketResult<CricketMatch>> RecordBallAsync(int matchId, RecordBallRequest request);
    Task<CricketResult<CricketMatch>> UndoLastBallAsync(int matchId);
    Task<CricketResult<CricketMatch>> SetBatterAsync(int matchId, NewBatterRequest request);
    Task<CricketResult<CricketMatch>> SetBowlerAsync(int matchId, NewBowlerRequest request);
    Task<CricketResult<CricketMatch>> EndInningsAsync(int matchId, EndInningsRequest request);
    Task<CricketResult<CricketMatch>> ReduceOversAsync(int matchId, ReduceOversRequest request);
    Task<CricketResult<CricketMatch>> EnforceFollowOnAsync(int matchId);
    Task<CricketResult<CricketMatch>> StartSuperOverAsync(int matchId);
    Task<CricketResult<CricketMatch>> CompleteAsync(int matchId, CompleteCricketMatchRequest request);

    Task<CricketScorecardDto?> BuildScorecardAsync(int matchId);

    /// <summary>
    /// The match as a scoring screen needs it, including what is legal next. Pure — it reads the
    /// already-loaded match, so every endpoint can answer with the same shape.
    /// </summary>
    CricketMatchStateDto BuildState(CricketMatch match);
}
