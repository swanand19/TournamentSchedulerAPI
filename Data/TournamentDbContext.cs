using Microsoft.EntityFrameworkCore;
using TournamentScheduler.Api.Models;
using TournamentScheduler.Api.Models.Accounts;
using TournamentScheduler.Api.Models.Cricket;
using TournamentScheduler.Api.Models.Messaging;
using TournamentScheduler.Api.Models.Notifications;
using TournamentScheduler.Api.Models.Versions;

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
    public DbSet<Models.Knockouts.KnockoutFixture> KnockoutFixtures => Set<Models.Knockouts.KnockoutFixture>();
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
    public DbSet<TeamAdmin> TeamAdmins => Set<TeamAdmin>();
    public DbSet<Models.Clubs.Club> Clubs => Set<Models.Clubs.Club>();
    public DbSet<Models.Clubs.ClubAdmin> ClubAdmins => Set<Models.Clubs.ClubAdmin>();
    public DbSet<Models.Clubs.ClubPlayer> ClubPlayers => Set<Models.Clubs.ClubPlayer>();
    public DbSet<Models.Clubs.TournamentEntry> TournamentEntries => Set<Models.Clubs.TournamentEntry>();
    public DbSet<Models.Clubs.TournamentEntryPlayer> TournamentEntryPlayers => Set<Models.Clubs.TournamentEntryPlayer>();
    public DbSet<TournamentInvite> TournamentInvites => Set<TournamentInvite>();
    public DbSet<Models.Public.TournamentFollow> TournamentFollows => Set<Models.Public.TournamentFollow>();
    public DbSet<Models.People.UserFollow> UserFollows => Set<Models.People.UserFollow>();
    public DbSet<Models.Leagues.League> Leagues => Set<Models.Leagues.League>();
    public DbSet<Models.Leagues.LeagueOrganiser> LeagueOrganisers => Set<Models.Leagues.LeagueOrganiser>();
    public DbSet<UserCricketProfile> UserCricketProfiles => Set<UserCricketProfile>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<AppStart> AppStarts => Set<AppStart>();
    public DbSet<Models.Auctions.PlayerPool> PlayerPools => Set<Models.Auctions.PlayerPool>();
    public DbSet<Models.Auctions.PoolTier> PoolTiers => Set<Models.Auctions.PoolTier>();
    public DbSet<Models.Auctions.PoolPlayer> PoolPlayers => Set<Models.Auctions.PoolPlayer>();
    public DbSet<Models.Auctions.Auction> Auctions => Set<Models.Auctions.Auction>();
    public DbSet<Models.Auctions.AuctionTeam> AuctionTeams => Set<Models.Auctions.AuctionTeam>();
    public DbSet<Models.Auctions.AuctionLot> AuctionLots => Set<Models.Auctions.AuctionLot>();
    public DbSet<Models.Auctions.AuctionBid> AuctionBids => Set<Models.Auctions.AuctionBid>();
    public DbSet<Models.Auctions.TeamDraw> TeamDraws => Set<Models.Auctions.TeamDraw>();
    public DbSet<Models.Auctions.DrawPick> DrawPicks => Set<Models.Auctions.DrawPick>();

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

        // Not unique: in amateur sides two players may wear the same number (decided 2026-10-21).
        // The index stays because squads are listed by number.
        modelBuilder.Entity<Player>()
            .HasIndex(p => new { p.TeamId, p.JerseyNumber });

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

        // Pictures (HLD P-26): file names in the pictures folder, "32 hex digits.jpg".
        modelBuilder.Entity<Tournament>().Property(t => t.LogoId).HasMaxLength(40).IsUnicode(false);
        modelBuilder.Entity<Tournament>().Property(t => t.BannerId).HasMaxLength(40).IsUnicode(false);

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

        // --- Knockouts ----------------------------------------------------
        // The tournament owns its knockout fixtures; a match playing one links to it with Restrict,
        // for the same multiple-cascade-path reason as SavedFixture (the match is the tournament's too).

        modelBuilder.Entity<Models.Knockouts.KnockoutFixture>(fixture =>
        {
            fixture.HasOne(f => f.Tournament)
                .WithMany()
                .HasForeignKey(f => f.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);
            fixture.HasIndex(f => new { f.TournamentId, f.Order }).IsUnique();
            fixture.Property(f => f.Label).HasMaxLength(60);
            fixture.OwnsOne(f => f.Home, slot => slot.Property(s => s.GroupName).HasMaxLength(20));
            fixture.OwnsOne(f => f.Away, slot => slot.Property(s => s.GroupName).HasMaxLength(20));
        });

        modelBuilder.Entity<Match>()
            .HasOne(m => m.KnockoutFixture)
            .WithMany()
            .HasForeignKey(m => m.KnockoutFixtureId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Match>().Property(m => m.RoundLabel).HasMaxLength(60);

        modelBuilder.Entity<CricketMatch>()
            .HasOne(m => m.KnockoutFixture)
            .WithMany()
            .HasForeignKey(m => m.KnockoutFixtureId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CricketMatch>().Property(m => m.RoundLabel).HasMaxLength(60);

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
            e.Property(u => u.PhotoId).HasMaxLength(40).IsUnicode(false);

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
            e.Property(s => s.AppVersion).HasMaxLength(20);
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

            // Public live scores (6E): found by the code in the link.
            e.Property(t => t.PublicCode).HasMaxLength(8);
            e.HasIndex(t => t.PublicCode).IsUnique().HasFilter("[PublicCode] IS NOT NULL");

            // Lasting teams entering with a code (2026-11-21, step 3).
            e.Property(t => t.EntryCode).HasMaxLength(9).IsUnicode(false);
            e.HasIndex(t => t.EntryCode).IsUnique().HasFilter("[EntryCode] IS NOT NULL");
        });

        modelBuilder.Entity<Models.Public.TournamentFollow>(e =>
        {
            e.ToTable("TournamentFollows");
            e.HasKey(f => f.TournamentFollowId);
            e.HasIndex(f => new { f.TournamentId, f.UserId }).IsUnique();
            e.HasIndex(f => f.UserId); // "my tournaments"

            e.HasOne<Tournament>().WithMany().HasForeignKey(f => f.TournamentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        // People following people (2026-11-23): one row a pair. Two links into Users, so neither cascades (SQL Server
        // refuses two paths); account deletion removes a person's rows itself.
        modelBuilder.Entity<Models.People.UserFollow>(e =>
        {
            e.ToTable("UserFollows");
            e.HasKey(f => new { f.FollowerUserId, f.FollowedUserId });
            e.HasIndex(f => f.FollowedUserId); // someone's followers, and the "is playing" news
            e.HasOne(f => f.Follower).WithMany().HasForeignKey(f => f.FollowerUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(f => f.Followed).WithMany().HasForeignKey(f => f.FollowedUserId).OnDelete(DeleteBehavior.Restrict);
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

        // Team admins (2026-11-20): run one team's squad. Removed with the team; the tournament id is a copy (no
        // second cascade path into the table).
        modelBuilder.Entity<TeamAdmin>(e =>
        {
            e.ToTable("TeamAdmins");
            e.HasKey(a => a.TeamAdminId);
            e.HasIndex(a => new { a.TeamId, a.UserId }).IsUnique();
            e.HasIndex(a => new { a.TournamentId, a.UserId }); // the access check and "my tournaments"
            e.HasIndex(a => a.UserId);

            e.HasOne<Team>().WithMany().HasForeignKey(a => a.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        // Lasting teams (2026-11-20, step 2 of docs/reusable-teams-plan.md): a club, its admins and players, outside any
        // tournament. Its squads are Teams with ClubId; the links from tournament rows are NoAction (the cascade-paths
        // rule), and ClubService clears them before a club or club player goes.
        modelBuilder.Entity<Models.Clubs.Club>(e =>
        {
            e.ToTable("Clubs");
            e.HasKey(c => c.ClubId);
            e.Property(c => c.Name).HasMaxLength(Models.Clubs.Club.NameMax).IsRequired();
            e.Property(c => c.Area).HasMaxLength(Models.Clubs.Club.AreaMax);
            e.Property(c => c.Sport).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.JoinCode).HasMaxLength(8).IsUnicode(false).IsRequired();
            e.Property(c => c.LogoId).HasMaxLength(40).IsUnicode(false);
            e.HasIndex(c => c.JoinCode).IsUnique();
            e.Property(c => c.PublicCode).HasMaxLength(8).IsUnicode(false);
            e.HasIndex(c => c.PublicCode).IsUnique().HasFilter("[PublicCode] IS NOT NULL");
        });

        modelBuilder.Entity<Models.Clubs.ClubAdmin>(e =>
        {
            e.ToTable("ClubAdmins");
            e.HasKey(a => a.ClubAdminId);
            e.HasIndex(a => new { a.ClubId, a.UserId }).IsUnique();
            e.HasIndex(a => a.UserId); // "my teams" and the access check
            e.HasOne(a => a.Club).WithMany(c => c.Admins).HasForeignKey(a => a.ClubId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Models.Clubs.ClubPlayer>(e =>
        {
            e.ToTable("ClubPlayers");
            e.HasKey(p => p.ClubPlayerId);
            e.Property(p => p.Name).HasMaxLength(100).IsRequired();
            e.Property(p => p.Position).HasMaxLength(50);
            e.Property(p => p.EmailId).HasMaxLength(256);
            e.Property(p => p.PhoneNumber).HasMaxLength(20);
            // A person is in a club once (and in any number of clubs, C-2).
            e.HasIndex(p => new { p.ClubId, p.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL");
            e.HasIndex(p => p.UserId);
            e.HasIndex(p => p.EmailId); // linked when that person signs up
            e.HasIndex(p => p.PhoneNumber);
            e.HasOne(p => p.Club).WithMany(c => c.Players).HasForeignKey(p => p.ClubId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
            e.OwnsOne(p => p.Cricket); // the same enum columns (numbers) as PlayerCricketProfiles
        });

        // Entries (2026-11-21, step 3). Tournament → Entry cascades; Club → Entry cascades too (another root, so no second
        // path). The made team and the ticked club players are plain numbers, not links: Tournament and Club already reach
        // those rows, and ClubSquads removes a club player's ticks before the club player goes.
        modelBuilder.Entity<Models.Clubs.TournamentEntry>(e =>
        {
            e.ToTable("TournamentEntries");
            e.HasKey(x => x.TournamentEntryId);
            e.Property(x => x.TeamName).HasMaxLength(Models.Clubs.TournamentEntry.TeamNameMax).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            // A club has one waiting-or-accepted entry per tournament.
            e.HasIndex(x => new { x.TournamentId, x.ClubId }).IsUnique().HasFilter("[Status] IN ('Waiting', 'Accepted')");
            e.HasIndex(x => x.ClubId);
            e.HasOne<Tournament>().WithMany().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Club).WithMany().HasForeignKey(x => x.ClubId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TeamId);
        });

        modelBuilder.Entity<Models.Clubs.TournamentEntryPlayer>(e =>
        {
            e.ToTable("TournamentEntryPlayers");
            e.HasKey(x => x.TournamentEntryPlayerId);
            e.HasIndex(x => new { x.TournamentEntryId, x.ClubPlayerId }).IsUnique();
            e.HasIndex(x => x.ClubPlayerId);
            e.HasOne(x => x.Entry).WithMany(x => x.Players).HasForeignKey(x => x.TournamentEntryId).OnDelete(DeleteBehavior.Cascade);
        });

        // Leagues and seasons (2026-11-24). A season is a tournament with LeagueId: NoAction (another root, the cascade-paths
        // rule), and LeagueService clears the seasons' link before a league goes. Organisers go with their league.
        modelBuilder.Entity<Models.Leagues.League>(e =>
        {
            e.ToTable("Leagues");
            e.HasKey(l => l.LeagueId);
            e.Property(l => l.Name).HasMaxLength(Models.Leagues.League.NameMax).IsRequired();
            e.Property(l => l.ShortName).HasMaxLength(Models.Leagues.League.ShortNameMax).IsRequired();
            e.Property(l => l.Area).HasMaxLength(Models.Leagues.League.AreaMax);
            e.Property(l => l.Sport).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.LogoId).HasMaxLength(40).IsUnicode(false);
            e.Property(l => l.PublicCode).HasMaxLength(8).IsUnicode(false).IsRequired();
            e.HasIndex(l => l.PublicCode).IsUnique();
        });

        modelBuilder.Entity<Models.Leagues.LeagueOrganiser>(e =>
        {
            e.ToTable("LeagueOrganisers");
            e.HasKey(o => o.LeagueOrganiserId);
            e.HasIndex(o => new { o.LeagueId, o.UserId }).IsUnique();
            e.HasIndex(o => o.UserId); // "leagues I run"
            e.HasOne(o => o.League).WithMany(l => l.Organisers).HasForeignKey(o => o.LeagueId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(o => o.User).WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Tournament>()
            .HasOne(t => t.LeagueRow).WithMany().HasForeignKey(t => t.LeagueId).OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Tournament>().HasIndex(t => t.LeagueId);
        modelBuilder.Entity<Team>().HasIndex(t => t.BroughtBackFromTeamId);

        modelBuilder.Entity<Team>()
            .HasOne(t => t.Club).WithMany().HasForeignKey(t => t.ClubId).OnDelete(DeleteBehavior.ClientSetNull);
        modelBuilder.Entity<Player>()
            .HasOne(p => p.ClubPlayer).WithMany().HasForeignKey(p => p.ClubPlayerId).OnDelete(DeleteBehavior.ClientSetNull);

        // Invites for people without an account yet (phase 6C).
        modelBuilder.Entity<TournamentInvite>(e =>
        {
            e.ToTable("TournamentInvites");
            e.HasKey(i => i.TournamentInviteId);
            e.Property(i => i.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Target).HasMaxLength(256).IsRequired();
            e.Property(i => i.MemberRole).HasConversion<string>().HasMaxLength(20);

            // One open invite per person per tournament; accepted and cancelled ones are kept for the record.
            e.HasIndex(i => new { i.TournamentId, i.Target }).IsUnique().HasFilter("[AcceptedAt] IS NULL AND [IsDisabled] = 0");
            e.HasIndex(i => i.Target); // found when that person signs up

            e.HasOne<Tournament>().WithMany().HasForeignKey(i => i.TournamentId).OnDelete(DeleteBehavior.Cascade);
        });

        // The inbox (phase 6D).
        modelBuilder.Entity<Notification>(e =>
        {
            e.ToTable("Notifications");
            e.HasKey(n => n.NotificationId);
            e.Property(n => n.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(n => n.Sport).HasConversion<string>().HasMaxLength(20);
            e.Property(n => n.Title).HasMaxLength(200).IsRequired();
            e.Property(n => n.Body).HasMaxLength(1000).IsRequired();
            e.Property(n => n.DedupeKey).HasMaxLength(100);
            e.Property(n => n.EmailTo).HasMaxLength(256);
            e.Property(n => n.EmailSubject).HasMaxLength(200);
            e.Property(n => n.EmailBody).HasMaxLength(4000);

            e.HasIndex(n => new { n.UserId, n.NotificationId }); // a person's inbox, newest first
            e.HasIndex(n => n.DedupeKey).HasFilter("[DedupeKey] IS NOT NULL");
            e.HasIndex(n => n.CreatedAt); // the hourly purge

            e.HasIndex(n => n.EmailSentAt).HasFilter("[EmailTo] IS NOT NULL AND [EmailSentAt] IS NULL"); // emails waiting to go

            e.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // The wording of every message, editable in the admin portal (phase 6D).
        modelBuilder.Entity<NotificationTemplate>(e =>
        {
            e.ToTable("NotificationTemplates");
            e.HasKey(t => t.NotificationTemplateId);
            e.Property(t => t.Code).HasConversion<string>().HasMaxLength(50);
            e.Property(t => t.EmailSubject).HasMaxLength(200);
            e.Property(t => t.EmailBody).HasMaxLength(4000);
            e.Property(t => t.SmsBody).HasMaxLength(1000);
            e.Property(t => t.SmsDltTemplateId).HasMaxLength(50);
            e.Property(t => t.WhatsAppTemplateName).HasMaxLength(512);
            e.Property(t => t.WhatsAppLanguage).HasMaxLength(15);
            e.Property(t => t.WhatsAppParameters).HasMaxLength(500);
            e.Property(t => t.NotificationTitle).HasMaxLength(200);
            e.Property(t => t.NotificationBody).HasMaxLength(1000);
            e.Property(t => t.PushTitle).HasMaxLength(100);
            e.Property(t => t.PushBody).HasMaxLength(500);
            e.HasIndex(t => t.Code).IsUnique();
        });

        // Released app versions and forced updates, kept in the admin portal (HLD P-25).
        modelBuilder.Entity<AppStart>(e =>
        {
            e.ToTable("AppStarts");
            e.HasKey(a => a.AppStartId);
            e.Property(a => a.Platform).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.AppVersion).HasMaxLength(20).IsRequired();
            e.Property(a => a.ApiVersion).HasMaxLength(20);
            e.Property(a => a.Description).HasMaxLength(500);
            e.Property(a => a.StoreUrl).HasMaxLength(500);
            e.HasIndex(a => new { a.Platform, a.AppVersion }).IsUnique();
        });

        // --- Players are people (phase 4) ------------------------------------

        modelBuilder.Entity<Player>(e =>
        {
            e.Property(p => p.EmailId).HasMaxLength(256);
            e.Property(p => p.PhoneNumber).HasMaxLength(20);

            // One place per person per tournament.
            e.HasIndex(p => new { p.TournamentId, p.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL");
            e.HasIndex(p => p.UserId);   // "tournaments I play in", renames
            e.HasIndex(p => p.EmailId);  // linking when someone signs up
            e.HasIndex(p => p.PhoneNumber);

            // Accounts are never deleted (they're anonymised), so this never has to cascade.
            e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.Property(t => t.JoinCode).HasMaxLength(6).IsUnicode(false);
            e.HasIndex(t => t.JoinCode).IsUnique().HasFilter("[JoinCode] IS NOT NULL");
        });

        ConfigureAuctions(modelBuilder);

        modelBuilder.Entity<UserCricketProfile>(e =>
        {
            e.ToTable("UserCricketProfiles");
            e.HasKey(p => p.UserId);
            e.HasOne<User>().WithOne().HasForeignKey<UserCricketProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>
    /// The player pool and the auction (2026-11-17). The tournament owns the pool, its tiers and the
    /// auction; the auction owns its teams' rows and lots; a lot owns its bids. Links to teams, pool
    /// players and squad places are plain ids, never foreign keys: each would be a second cascade path
    /// from the tournament, which SQL Server rejects, and the services keep them true.
    /// </summary>
    private static void ConfigureAuctions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Models.Auctions.PlayerPool>(e =>
        {
            e.ToTable("PlayerPools");
            e.HasKey(p => p.TournamentId);
            e.Property(p => p.JoinCode).HasMaxLength(7).IsUnicode(false);
            e.HasIndex(p => p.JoinCode).IsUnique().HasFilter("[JoinCode] IS NOT NULL");
            e.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Tournament>().WithOne().HasForeignKey<Models.Auctions.PlayerPool>(p => p.TournamentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Models.Auctions.PoolTier>(e =>
        {
            e.ToTable("PoolTiers");
            e.Property(t => t.Name).HasMaxLength(30).IsRequired();
            e.HasIndex(t => new { t.TournamentId, t.Order });
            e.HasOne<Tournament>().WithMany().HasForeignKey(t => t.TournamentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Models.Auctions.PoolPlayer>(e =>
        {
            e.ToTable("PoolPlayers");
            e.Property(p => p.Name).HasMaxLength(100).IsRequired();
            e.Property(p => p.EmailId).HasMaxLength(256);
            e.Property(p => p.PhoneNumber).HasMaxLength(20);
            e.Property(p => p.Position).HasMaxLength(40);
            e.Property(p => p.CricketRole).HasConversion<string>().HasMaxLength(30);

            // One pool place per person per tournament.
            e.HasIndex(p => new { p.TournamentId, p.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL");
            e.HasIndex(p => p.UserId);      // "my tournaments", who may watch
            e.HasIndex(p => p.EmailId);     // linking when someone signs up
            e.HasIndex(p => p.PhoneNumber);

            e.HasOne<Tournament>().WithMany().HasForeignKey(p => p.TournamentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Models.Auctions.Auction>(e =>
        {
            e.ToTable("Auctions");
            e.HasIndex(a => a.TournamentId).IsUnique();
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.Order).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.UnsoldPlan).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.Unit).HasMaxLength(8).IsRequired();
            e.Property(a => a.BidSteps).HasMaxLength(200).IsRequired();
            e.HasOne<Tournament>().WithMany().HasForeignKey(a => a.TournamentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Models.Auctions.AuctionTeam>(e =>
        {
            e.ToTable("AuctionTeams");
            e.HasIndex(t => new { t.AuctionId, t.TeamId }).IsUnique();
            e.HasIndex(t => new { t.TournamentId, t.BidderUserId }); // who may watch
            e.HasOne<Models.Auctions.Auction>().WithMany().HasForeignKey(t => t.AuctionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Models.Auctions.AuctionLot>(e =>
        {
            e.ToTable("AuctionLots");
            e.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(l => new { l.AuctionId, l.Round, l.Order });
            e.HasIndex(l => new { l.Status, l.ClosesAt }); // the clock job after a restart
            e.HasOne<Models.Auctions.Auction>().WithMany().HasForeignKey(l => l.AuctionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Models.Auctions.AuctionBid>(e =>
        {
            e.ToTable("AuctionBids");
            e.HasIndex(b => new { b.LotId, b.Id });
            e.HasOne<Models.Auctions.AuctionLot>().WithMany().HasForeignKey(b => b.LotId).OnDelete(DeleteBehavior.Cascade);
        });

        // The team draw (2026-11-17): one per tournament; its picks go with it.
        modelBuilder.Entity<Models.Auctions.TeamDraw>(e =>
        {
            e.ToTable("TeamDraws");
            e.HasIndex(d => d.TournamentId).IsUnique();
            e.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.TeamOrder).HasMaxLength(1000).IsRequired();
            e.HasIndex(d => new { d.Status, d.NextAutoAt }); // the clock job after a restart
            e.HasOne<Tournament>().WithMany().HasForeignKey(d => d.TournamentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Models.Auctions.DrawPick>(e =>
        {
            e.ToTable("DrawPicks");
            e.HasIndex(p => new { p.DrawId, p.PoolPlayerId }).IsUnique(); // drawn once
            e.HasIndex(p => new { p.DrawId, p.Order });
            e.HasIndex(p => p.TournamentId);
            e.HasOne<Models.Auctions.TeamDraw>().WithMany().HasForeignKey(p => p.DrawId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}