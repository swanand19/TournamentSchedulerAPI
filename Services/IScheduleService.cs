using TournamentScheduler.Api.Models;

namespace TournamentScheduler.Api.Services;

public interface IScheduleService
{
    List<Group> RandomizeGroups(List<string> teams, int groupCount);
    GroupSchedule GenerateGroupSchedule(string groupName, List<string> teams, int matchesPerTeam, bool allowRepeatFixtures);
    TournamentSchedule GenerateTournamentSchedule(List<GroupInput> groups, int matchesPerTeam, bool allowRepeatFixtures);
    Task<SavedSchedule> ApproveScheduleAsync(int tournamentId, TournamentSchedule schedule);
}
