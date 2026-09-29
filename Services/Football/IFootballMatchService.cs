using TournamentScheduler.Api.Models;

namespace TournamentScheduler.Api.Services.Football;

/// <summary>Everything that can happen to a football match. Each action answers with the JSON its endpoint returns.</summary>
public interface IFootballMatchService
{
    Task<ServiceResult<object>> GetMatch(int id);
    Task<ServiceResult<object>> SetupAndStart(int id, StartMatchRequest request);
    Task<ServiceResult<object>> RecordEvent(int id, RecordEventRequest request);
    Task<ServiceResult<object>> GetEvents(int id);
    Task<ServiceResult<object>> PauseClock(int id);
    Task<ServiceResult<object>> ResumeClock(int id);
    Task<ServiceResult<object>> AddTime(int id, AddTimeRequest request);
    Task<ServiceResult<object>> EndPeriod(int id);
    Task<ServiceResult<object>> NextHalf(int id);
    Task<ServiceResult<object>> CompleteMatch(int id, CompleteMatchRequest? request = null);
    Task<ServiceResult<object>> StartPenalties(int id, StartPenaltiesRequest request);
    Task<ServiceResult<object>> GetPenalties(int id);
    Task<ServiceResult<object>> RecordPenaltyKick(int id, RecordPenaltyKickRequest request);
    Task<ServiceResult<object>> EndPenaltiesManually(int id, EndPenaltiesRequest request);
}
