using Microsoft.EntityFrameworkCore;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;

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
    public DbSet<CricketMatch> CricketMatches => Set<CricketMatch>();
    public DbSet<PlayerCricketProfile> PlayerCricketProfiles => Set<PlayerCricketProfile>();
    public DbSet<CricketMatchPlayer> CricketMatchPlayers => Set<CricketMatchPlayer>();
    public DbSet<CricketInnings> CricketInnings => Set<CricketInnings>();
    public DbSet<CricketBall> CricketBalls => Set<CricketBall>();
    public DbSet<CricketMatchEvent> CricketMatchEvents => Set<CricketMatchEvent>();
    public DbSet<CricketInterruption> CricketInterruptions => Set<CricketInterruption>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<TournamentMember> TournamentMembers => Set<TournamentMember>();
    public DbSet<UserCricketProfile> UserCricketProfiles => Set<UserCricketProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAccounts(modelBuilder);

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

        // The cricketing half of a player, keyed on the player itself so there can only ever be
        // one, and removed with them.
        modelBuilder.Entity<PlayerCricketProfile>()
            .HasKey(p => p.PlayerId);

        modelBuilder.Entity<PlayerCricketProfile>()
            .HasOne(p => p.Player)
            .WithOne(p => p.Cricket)
            .HasForeignKey<PlayerCricketProfile>(p => p.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // --- Cricket ---------------------------------------------------
        // Deliberately the same cascade shape as Match above: the tournament owns its fixtures,
        // but the links out to SavedFixture and Team are Restrict, because Tournament already
        // cascades to Team and to its fixtures independently and SQL Server rejects the resulting
        // multiple cascade paths.

        modelBuilder.Entity<Tournament>()
            .HasMany(t => t.CricketMatches)
            .WithOne(m => m.Tournament)
            .HasForeignKey(m => m.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CricketMatch>()
            .HasOne(m => m.SavedFixture)
            .WithMany()
            .HasForeignKey(m => m.SavedFixtureId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CricketMatch>()
            .HasOne(m => m.HomeTeam)
            .WithMany()
            .HasForeignKey(m => m.HomeTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CricketMatch>()
            .HasOne(m => m.AwayTeam)
            .WithMany()
            .HasForeignKey(m => m.AwayTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        // The match owns its squad, its innings and its timeline; the innings owns its deliveries.
        // Links out to Player and Team stay Restrict for the same multiple-cascade-path reason.

        modelBuilder.Entity<CricketMatch>()
            .HasMany(m => m.Squad)
            .WithOne(p => p.Match)
            .HasForeignKey(p => p.CricketMatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CricketMatch>()
            .HasMany(m => m.Innings)
            .WithOne(i => i.Match)
            .HasForeignKey(i => i.CricketMatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CricketMatch>()
            .HasMany(m => m.Events)
            .WithOne(e => e.Match)
            .HasForeignKey(e => e.CricketMatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CricketInnings>()
            .HasMany(i => i.Balls)
            .WithOne(b => b.Innings)
            .HasForeignKey(b => b.InningsId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CricketInnings>()
            .HasMany(i => i.Interruptions)
            .WithOne(x => x.Innings)
            .HasForeignKey(x => x.InningsId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CricketMatchPlayer>()
            .HasOne(p => p.Player)
            .WithMany()
            .HasForeignKey(p => p.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CricketMatchPlayer>()
            .HasOne(p => p.Team)
            .WithMany()
            .HasForeignKey(p => p.TeamId)
            .OnDelete(DeleteBehavior.Restrict);

        // A player appears once per match squad, and deliveries are read in the order bowled.
        modelBuilder.Entity<CricketMatchPlayer>()
            .HasIndex(p => new { p.CricketMatchId, p.PlayerId })
            .IsUnique();

        modelBuilder.Entity<CricketInnings>()
            .HasIndex(i => new { i.CricketMatchId, i.InningsNumber })
            .IsUnique();

        modelBuilder.Entity<CricketBall>()
            .HasIndex(b => new { b.InningsId, b.SequenceNumber })
            .IsUnique();
    }

    // --- Accounts ------------------------------------------------------
    // Table and column names follow the team's conventions (Crd, CrdBy, Lmd, LmdBy, IsDisabled).
    // See docs/accounts-and-access-plan.md.

    /// <summary>When the seed rows were written; fixed, so the model doesn't change on every build.</summary>
    private static readonly DateTime SeededAt = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static void ConfigureAccounts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("Roles");
            e.HasKey(r => r.RoleId);
            e.Property(r => r.RoleName).HasMaxLength(100).IsRequired();
            e.Property(r => r.Description).HasMaxLength(500);
            e.HasIndex(r => r.RoleName).IsUnique();
            e.HasData(
                new Role { RoleId = RoleIds.Admin, RoleName = "Admin", Description = "Runs the admin portal.", Crd = SeededAt, CrdBy = SystemUser.Id, Lmd = SeededAt, LmdBy = SystemUser.Id },
                new Role { RoleId = RoleIds.EndUser, RoleName = "EndUser", Description = "Everyone who signs up in the apps.", Crd = SeededAt, CrdBy = SystemUser.Id, Lmd = SeededAt, LmdBy = SystemUser.Id });
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(u => u.UserId);
            e.Property(u => u.Name).HasMaxLength(100).IsRequired();
            e.Property(u => u.EmailId).HasMaxLength(256);
            e.Property(u => u.PasswordHash).HasMaxLength(200).IsUnicode(false);
            e.Property(u => u.PhoneNumber).HasMaxLength(20);

            // Unique only among accounts that have one: deleted accounts all have none.
            e.HasIndex(u => u.EmailId).IsUnique().HasFilter("[EmailId] IS NOT NULL");
            e.HasIndex(u => u.PhoneNumber).IsUnique().HasFilter("[PhoneNumber] IS NOT NULL");

            e.HasOne(u => u.Role)
                .WithMany()
                .HasForeignKey(u => u.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasData(new User
            {
                UserId = SystemUser.Id,
                Name = "System",
                RoleId = RoleIds.Admin,
                Crd = SeededAt,
                CrdBy = SystemUser.Id,
                Lmd = SeededAt,
                LmdBy = SystemUser.Id
            });
        });

        modelBuilder.Entity<UserSession>(e =>
        {
            e.ToTable("UserSessions");
            e.HasKey(s => s.SessionId);
            e.Property(s => s.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
            e.Property(s => s.Channel).HasMaxLength(30);
            e.Property(s => s.DeviceId).HasMaxLength(100);
            e.Property(s => s.RevokedReason).HasMaxLength(100);
            e.HasIndex(s => s.TokenHash).IsUnique();
            e.HasIndex(s => s.UserId);

            e.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OtpChallenge>(e =>
        {
            e.ToTable("OtpChallenges");
            e.HasKey(o => o.OtpChallengeId);
            e.Property(o => o.Purpose).HasConversion<string>().HasMaxLength(30);
            e.Property(o => o.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.Target).HasMaxLength(256).IsRequired();
            e.Property(o => o.CodeHash).HasMaxLength(64).IsUnicode(false).IsRequired();
            e.Property(o => o.DeviceId).HasMaxLength(100);
            e.Property(o => o.ClientIp).HasMaxLength(64);

            // The send limits count recent codes per email, per device and per address.
            e.HasIndex(o => new { o.Target, o.Crd });
            e.HasIndex(o => new { o.DeviceId, o.Crd });
            e.HasIndex(o => new { o.ClientIp, o.Crd });
        });

        modelBuilder.Entity<Permission>(e =>
        {
            e.ToTable("Permissions");
            e.HasKey(p => p.PermissionId);
            e.Property(p => p.ServiceRequestId).HasMaxLength(100).IsRequired();
            e.Property(p => p.ControllerName).HasMaxLength(100).IsRequired();
            e.Property(p => p.ActionName).HasMaxLength(100).IsRequired();
            e.Property(p => p.HttpMethod).HasMaxLength(10).IsRequired();
            e.Property(p => p.URL).HasMaxLength(300).IsRequired();
            e.HasIndex(p => p.ServiceRequestId).IsUnique();
        });

        modelBuilder.Entity<RolePermission>(e =>
        {
            e.ToTable("RolePermissions");
            e.HasKey(rp => rp.RolePermissionId);
            e.HasIndex(rp => new { rp.RoleId, rp.PermissionId }).IsUnique();

            e.HasOne<Role>().WithMany().HasForeignKey(rp => rp.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Permission>().WithMany().HasForeignKey(rp => rp.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- Who may do what in a tournament (phase 3) ---------------------

        modelBuilder.Entity<Tournament>(e =>
        {
            // Existing tournaments belong to System until someone takes them over (deploy/sql/04-…).
            e.Property(t => t.CreatedByUserId).HasDefaultValue(SystemUser.Id);
            e.Property(t => t.TimeZoneId).HasMaxLength(64).IsRequired().HasDefaultValue(Tournament.DefaultTimeZoneId);
            e.HasIndex(t => new { t.Status, t.EndDate }); // the hourly completion job
        });

        modelBuilder.Entity<TournamentMember>(e =>
        {
            e.ToTable("TournamentMembers");
            e.HasKey(m => m.TournamentMemberId);
            e.Property(m => m.MemberRole).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(m => new { m.TournamentId, m.UserId }).IsUnique();
            e.HasIndex(m => m.UserId); // "my tournaments"

            e.HasOne<Tournament>().WithMany().HasForeignKey(m => m.TournamentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        // --- Players are people (phase 4) ------------------------------------

        modelBuilder.Entity<Player>(e =>
        {
            e.Property(p => p.EmailId).HasMaxLength(256);

            // One place per person per tournament.
            e.HasIndex(p => new { p.TournamentId, p.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL");
            e.HasIndex(p => p.UserId);   // "tournaments I play in", renames
            e.HasIndex(p => p.EmailId);  // linking when someone signs up

            // Accounts are never deleted (they're anonymised), so this never has to cascade.
            e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.Property(t => t.JoinCode).HasMaxLength(6).IsUnicode(false);
            e.HasIndex(t => t.JoinCode).IsUnique().HasFilter("[JoinCode] IS NOT NULL");
        });

        modelBuilder.Entity<UserCricketProfile>(e =>
        {
            e.ToTable("UserCricketProfiles");
            e.HasKey(p => p.UserId);
            e.HasOne<User>().WithOne().HasForeignKey<UserCricketProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}