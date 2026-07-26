using TournamentScheduler.Api.Models;

namespace TournamentScheduler.Api.Services;

public interface IScheduleService
{
    List<Group> RandomizeGroups(List<string> teams, int groupCount);
    GroupSchedule GenerateGroupSchedule(string groupName, List<string> teams, int matchesPerTeam);
    TournamentSchedule GenerateTournamentSchedule(List<GroupInput> groups, int matchesPerTeam);
    Task<SavedSchedule> ApproveScheduleAsync(int tournamentId, TournamentSchedule schedule);
}