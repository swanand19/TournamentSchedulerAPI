using Microsoft.EntityFrameworkCore;
using TournamentScheduler.Api.Models;

namespace TournamentScheduler.Api.Data;

public class TournamentDbContext : DbContext
{
    public TournamentDbContext(DbContextOptions<TournamentDbContext> options) : base(options) { }

    public DbSet<SavedSchedule> SavedSchedules => Set<SavedSchedule>();
    public DbSet<SavedGroup> SavedGroups => Set<SavedGroup>();
    public DbSet<SavedFixture> SavedFixtures => Set<SavedFixture>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchPlayer> MatchPlayers => Set<MatchPlayer>();
    public DbSet<MatchEvent> MatchEvents => Set<MatchEvent>();
    public DbSet<PenaltyKick> PenaltyKicks => Set<PenaltyKick>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SavedSchedule>()
            .HasMany(s => s.Groups)
            .WithOne(g => g.SavedSchedule)
            .HasForeignKey(g => g.SavedScheduleId);

        modelBuilder.Entity<SavedGroup>()
            .HasMany(g => g.Fixtures)
            .WithOne(f => f.SavedGroup)
            .HasForeignKey(f => f.SavedGroupId);

        modelBuilder.Entity<Team>()
            .HasMany(t => t.Players)
            .WithOne(p => p.Team)
            .HasForeignKey(p => p.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        // Team is now scoped to a Tournament: uniqueness is per-tournament,
        // not global, and deleting a tournament removes its teams.
        modelBuilder.Entity<Team>()
            .HasOne(t => t.Tournament)
            .WithMany(t => t.Teams)
            .HasForeignKey(t => t.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Team>()
            .HasIndex(t => new { t.TournamentId, t.Name })
            .IsUnique();

        modelBuilder.Entity<Tournament>()
            .HasMany(t => t.Schedules)
            .WithOne(s => s.Tournament)
            .HasForeignKey(s => s.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Player>()
            .HasIndex(p => new { p.TeamId, p.JerseyNumber })
            .IsUnique()
            .HasFilter("[JerseyNumber] IS NOT NULL");

        modelBuilder.Entity<Tournament>()
            .HasMany(t => t.Matches)
            .WithOne(m => m.Tournament)
            .HasForeignKey(m => m.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict these to avoid multiple-cascade-path errors on SQL Server
        // (Tournament already cascades to Team and to Match independently)
        modelBuilder.Entity<Match>()
            .HasOne(m => m.SavedFixture)
            .WithMany()
            .HasForeignKey(m => m.SavedFixtureId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Match>()
            .HasOne(m => m.HomeTeam)
            .WithMany()
            .HasForeignKey(m => m.HomeTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Match>()
            .HasOne(m => m.AwayTeam)
            .WithMany()
            .HasForeignKey(m => m.AwayTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Match>()
            .HasMany(m => m.MatchPlayers)
            .WithOne(mp => mp.Match)
            .HasForeignKey(mp => mp.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Match>()
            .HasMany(m => m.Events)
            .WithOne(e => e.Match)
            .HasForeignKey(e => e.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MatchPlayer>()
            .HasOne(mp => mp.Player)
            .WithMany()
            .HasForeignKey(mp => mp.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MatchPlayer>()
            .HasOne(mp => mp.Team)
            .WithMany()
            .HasForeignKey(mp => mp.TeamId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Match>()
            .HasMany(m => m.PenaltyKicks)
            .WithOne(k => k.Match)
            .HasForeignKey(k => k.MatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}