namespace TournamentScheduler.Api.Gateway;

/// <summary>
/// The name an action is called by through the secure gateway, e.g. <c>CRICKET_BALL_RECORD</c>.
///
/// The apps never see a URL: they send this id in the encrypted request's header and the gateway
/// finds the action from it. That keeps the API's layout off the wire and lets a route be renamed
/// without touching either app. Ids are AREA_ACTION in capitals, unique across the API (the
/// architecture tests check), and must not change once an app uses them.
///
/// The action's route parameter names are part of the same contract: the app fills them by name
/// in the encrypted payload's <c>routeParams</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ServiceRequestIdAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}

/// <summary>
/// Lets an action (or every action of a controller) be called on its own URL with a plain body,
/// outside the encrypted gateway. Use sparingly — for infrastructure a script or a monitor calls,
/// never for anything that carries tournament data. It stays callable through the gateway too.
///
/// An action can also be exempted without a code change by listing its service id under
/// <c>Gateway:PlainServiceIds</c> in appsettings.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SkipEncryptionAttribute : Attribute;
