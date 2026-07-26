namespace TournamentScheduler.Api.Models
{
    public class Group
    {
        public string Name { get; set; } = string.Empty; // "A" or "B"
        public List<string> Teams { get; set; } = new();
    }
}
