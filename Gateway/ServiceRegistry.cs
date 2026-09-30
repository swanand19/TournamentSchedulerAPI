using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace TournamentScheduler.Api.Gateway;

/// <summary>One callable service: the action a service id names, and how to reach it.</summary>
public sealed record ServiceDescriptor(
    string Id,
    string HttpMethod,
    string RouteTemplate,
    bool SkipsEncryption,
    string ActionName);

/// <summary>
/// Every <see cref="ServiceRequestIdAttribute"/> in the API, read once from MVC's own action list,
/// so the gateway calls exactly the actions (and routes) that a direct call would.
/// </summary>
public sealed partial class ServiceRegistry
{
    private readonly Lazy<IReadOnlyDictionary<string, ServiceDescriptor>> _services;

    public ServiceRegistry(IActionDescriptorCollectionProvider actions, IOptions<GatewayOptions> options)
    {
        var plain = options.Value.PlainServiceIds.ToHashSet(StringComparer.Ordinal);
        _services = new Lazy<IReadOnlyDictionary<string, ServiceDescriptor>>(() => Build(actions, plain));
    }

    public IReadOnlyCollection<ServiceDescriptor> All => _services.Value.Values.ToList();

    public ServiceDescriptor? Find(string id) => _services.Value.GetValueOrDefault(id);

    private static IReadOnlyDictionary<string, ServiceDescriptor> Build(IActionDescriptorCollectionProvider actions, HashSet<string> plain)
    {
        var services = new Dictionary<string, ServiceDescriptor>(StringComparer.Ordinal);

        foreach (var action in actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            var id = action.MethodInfo.GetCustomAttribute<ServiceRequestIdAttribute>()?.Id;
            if (id == null) continue;

            var method = action.ActionConstraints?.OfType<HttpMethodActionConstraint>()
                .SelectMany(c => c.HttpMethods).SingleOrDefault()
                ?? throw new InvalidOperationException($"{Describe(action)} has service id {id} but no single HTTP method.");
            var template = action.AttributeRouteInfo?.Template
                ?? throw new InvalidOperationException($"{Describe(action)} has service id {id} but no attribute route.");

            if (services.TryGetValue(id, out var existing))
                throw new InvalidOperationException($"Service id {id} is on both {existing.ActionName} and {Describe(action)}.");

            var skips = action.MethodInfo.IsDefined(typeof(SkipEncryptionAttribute), inherit: true)
                        || action.ControllerTypeInfo.IsDefined(typeof(SkipEncryptionAttribute), inherit: true)
                        || plain.Contains(id);

            services[id] = new ServiceDescriptor(id, method, template, skips, Describe(action));
        }

        return services;
    }

    private static string Describe(ControllerActionDescriptor action) => $"{action.ControllerName}Controller.{action.ActionName}";

    /// <summary>
    /// The URL path for <paramref name="service"/> with its route parameters filled in, e.g.
    /// <c>api/cricket-matches/{matchId}/balls</c> → <c>/api/cricket-matches/42/balls</c>. Returns the
    /// name of the first missing parameter instead when one is absent.
    /// </summary>
    public static (string? Path, string? MissingParameter) FillRoute(ServiceDescriptor service, IReadOnlyDictionary<string, string> values)
    {
        string? missing = null;
        var path = RouteParameter().Replace(service.RouteTemplate, m =>
        {
            var name = m.Groups["name"].Value;
            if (values.TryGetValue(name, out var value)) return Uri.EscapeDataString(value);
            if (m.Groups["optional"].Success) return "";
            missing ??= name;
            return "";
        });

        return missing != null ? (null, missing) : ("/" + path.TrimEnd('/'), null);
    }

    // {name}, {name:int}, {name?}, {*rest} — everything a route template's parameter can look like.
    [GeneratedRegex(@"\{\*{0,2}(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::[^}?]*)?(?<optional>\?)?\}")]
    private static partial Regex RouteParameter();
}
