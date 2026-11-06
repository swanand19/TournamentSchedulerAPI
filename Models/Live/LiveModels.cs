namespace TournamentScheduler.Api.Models.Live;

/// <summary>Opens the live line once: <c>{hub address}{Path}?access_token={Ticket}</c>, within <see cref="ExpiresInSeconds"/>.</summary>
public sealed class LiveTicketDto
{
    public string Ticket { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }

    /// <summary>The hub's path on the API's own address ("/hubs/live").</summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>Stop hearing about a tournament (<see cref="TournamentId"/>) or one match (<see cref="Sport"/> + <see cref="MatchId"/>).</summary>
public sealed class LiveUnwatchRequest
{
    public int? TournamentId { get; set; }
    public string? Sport { get; set; }
    public int? MatchId { get; set; }
}
