using TournamentScheduler.Api.Models;

namespace TournamentScheduler.Api.Services;

public interface IScheduleService
{
    List<Group> RandomizeGroups(List<string> teams, int groupCount);
    GroupSchedule GenerateGroupSchedule(string groupName, List<string> teams, int matchesPerTeam, bool allowRepeatFixtures);
    TournamentSchedule GenerateTournamentSchedule(List<GroupInput> groups, int matchesPerTeam, bool allowRepeatFixtures);
    Task<SavedSchedule> ApproveScheduleAsync(int tournamentId, TournamentSchedule schedule);

    // The endpoints' actions: validate the request, then use the methods above.
    ServiceResult<object> DrawGroups(RandomizeGroupsRequest request);
    ServiceResult<object> CheckManualGroups(ManualGroupsRequest request);
    ServiceResult<TournamentSchedule> GenerateSchedule(GenerateScheduleRequest request);
    Task<ServiceResult<object>> ApproveAsync(ApproveScheduleWithTournamentRequest request);
    Task<ServiceResult<SavedSchedule>> GetSavedScheduleAsync(int id);
}
