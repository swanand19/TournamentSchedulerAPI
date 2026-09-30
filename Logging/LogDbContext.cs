using Microsoft.EntityFrameworkCore;

namespace TournamentScheduler.Api.Logging;

/// <summary>
/// The logs database — separate from the game's, so logs can be capped, purged and lost without a
/// thought for tournament data. Its migrations live in Logging/Migrations; add one with
/// <c>dotnet ef migrations add Name --context LogDbContext --output-dir Logging/Migrations</c>.
/// Only the Logging folder may use it (the architecture tests check).
/// </summary>
public class LogDbContext(DbContextOptions<LogDbContext> options) : DbContext(options)
{
    public DbSet<MBMiddlewareLog> MBMiddlewareLogs => Set<MBMiddlewareLog>();
    public DbSet<MBActivityLog> MBActivityLogs => Set<MBActivityLog>();
    public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();
    public DbSet<RemoteLog> RemoteLogs => Set<RemoteLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MBMiddlewareLog>(e =>
        {
            e.ToTable("MBMiddlewareLogs");
            e.HasKey(x => x.MBMiddlewareLogId);
            e.Property(x => x.ServiceRequestId).HasMaxLength(100);
            e.Property(x => x.JourneyId).HasMaxLength(100);
            e.Property(x => x.SessionId).HasMaxLength(100);
            e.Property(x => x.DeviceId).HasMaxLength(100);
            e.Property(x => x.Channel).HasMaxLength(30);
            e.Property(x => x.AppVersion).HasMaxLength(50);
            e.Property(x => x.KeyId).HasMaxLength(50);
            e.Property(x => x.ClientIp).HasMaxLength(64);
            e.Property(x => x.RejectedReason).HasMaxLength(500);
            e.Property(x => x.StartDate).HasColumnType("datetime2(3)");
            e.Property(x => x.EndDate).HasColumnType("datetime2(3)");
            e.HasIndex(x => x.RequestUUID);
            e.HasIndex(x => x.StartDate);
        });

        modelBuilder.Entity<MBActivityLog>(e =>
        {
            e.ToTable("MBActivityLogs");
            e.HasKey(x => x.MBActivityLogId);
            e.Property(x => x.SessionId).HasMaxLength(100);
            e.Property(x => x.JourneyId).HasMaxLength(100);
            e.Property(x => x.DeviceId).HasMaxLength(100);
            e.Property(x => x.Channel).HasMaxLength(30);
            e.Property(x => x.ServiceRequestId).HasMaxLength(100);
            e.Property(x => x.ControllerName).HasMaxLength(100);
            e.Property(x => x.ActionName).HasMaxLength(100);
            e.Property(x => x.HttpMethod).HasMaxLength(10);
            e.Property(x => x.Url).HasMaxLength(2048);
            e.Property(x => x.StatusMessage).HasMaxLength(1000);
            e.Property(x => x.StartDate).HasColumnType("datetime2(3)");
            e.Property(x => x.EndDate).HasColumnType("datetime2(3)");
            e.HasIndex(x => x.RequestUUID);
            e.HasIndex(x => x.StartDate);
        });

        modelBuilder.Entity<ErrorLog>(e =>
        {
            e.ToTable("ErrorLogs");
            e.HasKey(x => x.ErrorLogId);
            e.Property(x => x.ServiceRequestId).HasMaxLength(100);
            e.Property(x => x.Url).HasMaxLength(2048);
            e.Property(x => x.ExceptionType).HasMaxLength(500);
            e.Property(x => x.LogMessage).HasMaxLength(2000);
            e.Property(x => x.Source).HasMaxLength(500);
            e.Property(x => x.Severity).HasMaxLength(20);
            e.Property(x => x.Crd).HasColumnType("datetime2(3)");
            e.HasIndex(x => x.RequestUUID);
            e.HasIndex(x => x.Crd);
        });

        modelBuilder.Entity<RemoteLog>(e =>
        {
            e.ToTable("RemoteLogs");
            e.HasKey(x => x.RemoteLogId);
            e.Property(x => x.ProviderName).HasMaxLength(100);
            e.Property(x => x.HttpMethod).HasMaxLength(10);
            e.Property(x => x.Url).HasMaxLength(2048);
            e.Property(x => x.ErrorMessage).HasMaxLength(2000);
            e.Property(x => x.StartDate).HasColumnType("datetime2(3)");
            e.Property(x => x.EndDate).HasColumnType("datetime2(3)");
            e.HasIndex(x => x.RequestUUID);
            e.HasIndex(x => x.StartDate);
        });
    }
}
