using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TournamentScheduler.Api.Auth.Otp;
using TournamentScheduler.Api.Auth.Permissions;
using TournamentScheduler.Api.Logging;
using TournamentScheduler.Api.Messaging;
using TournamentScheduler.Api.Services;
using TournamentScheduler.Api.Services.Access;
using TournamentScheduler.Api.Services.Accounts;
using TournamentScheduler.Api.Services.Admin;
using TournamentScheduler.Api.Services.Notifications;

namespace TournamentScheduler.Api.Auth;

public static class AuthRegistration
{
    /// <summary>Accounts, sessions and one-time codes. See Auth/README.md.</summary>
    public static IServiceCollection AddAccounts(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.Section));
        services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.Section));
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());

        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IOtpCodeService, OtpCodeService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IAccountDeletionService, AccountDeletionService>();
        services.AddScoped<IUserAdminService, UserAdminService>();

        // Role permissions: synced with the code at start-up, cached, checked on every request.
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddSingleton<PermissionCache>();
        services.AddHostedService<PermissionSyncHostedService>();

        // Tournament access: owners, scorers, the scorer lock, and completing tournaments on time.
        services.AddScoped<TournamentContext>();
        services.AddScoped<ITournamentContext>(sp => sp.GetRequiredService<TournamentContext>());
        services.AddScoped<TournamentAccessService>();
        services.AddScoped<ITournamentAccessService>(sp => sp.GetRequiredService<TournamentAccessService>());
        services.AddScoped<IScoringLockService, ScoringLockService>();
        services.AddScoped<ITournamentMemberService, TournamentMemberService>();
        services.AddScoped<ITournamentInviteService, TournamentInviteService>();

        // The inbox (phase 6D).
        services.AddScoped<INotifier, Notifier>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddHostedService<NotificationEmailJob>();
        services.AddSingleton<NotificationTemplateStore>();
        services.AddScoped<IAdminTemplateService, AdminTemplateService>();

        // Players are people: squad places linked to accounts, by email or a team join code.
        services.AddScoped<IPlayerLinkService, PlayerLinkService>();
        services.AddScoped<ITeamJoinService, TeamJoinService>();
        services.AddSingleton<JoinCodeAttempts>();

        // The admin portal: its sign-in (code + password), permissions and roles, people, every tournament.
        services.AddScoped<PortalAccess>();
        services.AddScoped<IAdminSignInService, AdminSignInService>();
        services.AddScoped<IAdminPermissionService, AdminPermissionService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IAdminTournamentService, AdminTournamentService>();
        services.AddScoped<ITournamentLifecycleService, TournamentLifecycleService>();
        services.AddHostedService<TournamentLifecycleJob>();

        // Codes and other messages (invites) go out by email or text; the dispatchers pick the sender per message.
        services.AddSingleton<RemoteCallRecorder>();
        services.AddSingleton<SmtpMailer>();
        services.AddSingleton<IMessageSender, MessageDispatcher>();
        services.AddSingleton<SmtpOtpSender>();
        services.AddSingleton<LogOtpSender>();
        services.AddSingleton<IOtpSender, OtpDispatcher>();

        return services;
    }

    /// <summary>After routing and gateway enforcement: sets who is calling, and refuses anyone who must sign in first.</summary>
    public static IApplicationBuilder UseSessionAuthentication(this IApplicationBuilder app) =>
        app.UseMiddleware<SessionAuthenticationMiddleware>();

    /// <summary>After the session check: refuses a role that may not call the service at all.</summary>
    public static IApplicationBuilder UsePermissionAuthorization(this IApplicationBuilder app) =>
        app.UseMiddleware<PermissionAuthorizationMiddleware>();
}
