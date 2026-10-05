namespace TournamentScheduler.Api.Auth;

/// <summary>
/// Lets an action run without a signed-in user. Everything else answers 401 until the app signs in.
///
/// Only what a signed-out app must reach: the health check and the two sign-in steps. It is a
/// reviewed exception, like <see cref="Gateway.SkipEncryptionAttribute"/> — the architecture tests
/// list every service that has it, so adding one means changing that test on purpose. It says
/// nothing about encryption: an anonymous service still goes through the gateway.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AllowAnonymousServiceAttribute : Attribute;
