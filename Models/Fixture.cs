namespace TournamentScheduler.Api.Models
{
    public class Fixture
    {
        public int Id { get; set; }
        public string Home { get; set; } = string.Empty;
        public string Away { get; set; } = string.Empty;

        /// <summary>
        /// Matchday this fixture belongs to. Fixtures in the same round never share a team,
        /// so a round can be played in parallel (or back to back without anyone playing twice).
        /// </summary>
        public int Round { get; set; }
    }
}
