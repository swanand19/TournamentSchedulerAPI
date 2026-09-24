using Microsoft.EntityFrameworkCore;

namespace TournamentScheduler.Api.Models.Cricket;

/// <summary>
/// Everything about how a match is played, fixed when the scorer sets it up. Owned by
/// <see cref="CricketMatch"/> rather than flattened onto it because there are twenty of these and
/// they travel together — the setup request posts one of these, and the scoring engine reads one.
///
/// The defaults describe a standard T20; <see cref="CricketRulePresets"/> supplies the rest.
/// </summary>
[Owned]
public class CricketMatchRules
{
    public CricketFormat Format { get; set; } = CricketFormat.LimitedOvers;

    /// <summary>Innings each side bats: 1 for limited overs, 2 for the multi-innings formats.</summary>
    public int InningsPerSide { get; set; } = 1;

    /// <summary>Null means no over limit, which is only valid for a timed multi-innings match.</summary>
    public int? OversPerInnings { get; set; } = 20;

    /// <summary>Six almost everywhere; five or ten for The Hundred.</summary>
    public int BallsPerOver { get; set; } = 6;

    /// <summary>Null means uncapped, as in a timed match.</summary>
    public int? MaxOversPerBowler { get; set; } = 4;

    public int PlayersPerSide { get; set; } = 11;

    /// <summary>
    /// When true the last batter carries on alone rather than the innings ending — the usual
    /// gully and box-cricket rule. It changes when a side is "all out" and nothing else.
    /// </summary>
    public bool LastManStanding { get; set; }

    // --- Extras -------------------------------------------------------

    /// <summary>One in the laws; some tape-ball leagues use two.</summary>
    public int WidePenaltyRuns { get; set; } = 1;
    public int NoBallPenaltyRuns { get; set; } = 1;

    public bool FreeHitAfterNoBall { get; set; } = true;

    /// <summary>Off in the laws, but a common tape-ball and gully variation.</summary>
    public bool FreeHitAfterWide { get; set; }

    public bool ByesAllowed { get; set; } = true;
    public bool LegByesAllowed { get; set; } = true;
    public bool PenaltyRunsAllowed { get; set; } = true;
    public bool OverthrowsAllowed { get; set; } = true;

    /// <summary>Many tennis-ball and indoor leagues play without LBW.</summary>
    public bool LbwEnabled { get; set; } = true;

    /// <summary>
    /// Powerplay over ranges as "from-to" pairs, e.g. "1-6,16-20". Stored as text because it is a
    /// short display-only list; a table would be three joins for something never queried on.
    /// </summary>
    public string? PowerplayOvers { get; set; }

    // --- Resolution ---------------------------------------------------

    public TieResolution TieResolution { get; set; } = TieResolution.SuperOver;

    /// <summary>Only meaningful for the timed format, where running out of time is a draw.</summary>
    public bool DrawAllowed { get; set; }

    /// <summary>
    /// Lead after two innings at which the follow-on may be enforced. The laws scale this with
    /// match length: 200 for five days, 150 for three or four, 100 for two, 75 for one.
    /// </summary>
    public int? FollowOnMargin { get; set; }

    public bool DlsEnabled { get; set; }

    // --- Conditions, recorded for context and stats -------------------

    public BallType BallType { get; set; } = BallType.Leather;
    public PitchType PitchType { get; set; } = PitchType.Turf;

    /// <summary>A side is all out one short of its strength, unless the last batter bats alone.</summary>
    public int WicketsToEndInnings => LastManStanding ? PlayersPerSide : PlayersPerSide - 1;

    /// <summary>Total legal deliveries in a full innings, or null when there is no over limit.</summary>
    public int? BallsPerInnings =>
        OversPerInnings.HasValue ? OversPerInnings.Value * BallsPerOver : null;

    public bool IsMultiInnings => InningsPerSide > 1;
}

/// <summary>
/// Starting points for the setup screen. Every field stays editable afterwards — the presets exist
/// so the common case is two taps, not so that formats are locked down.
/// </summary>
public static class CricketRulePresets
{
    public static readonly IReadOnlyDictionary<string, Func<CricketMatchRules>> All =
        new Dictionary<string, Func<CricketMatchRules>>(StringComparer.OrdinalIgnoreCase)
        {
            ["T20"] = () => new CricketMatchRules
            {
                OversPerInnings = 20,
                MaxOversPerBowler = 4,
                PowerplayOvers = "1-6",
            },
            ["T10"] = () => new CricketMatchRules
            {
                OversPerInnings = 10,
                MaxOversPerBowler = 2,
                PowerplayOvers = "1-3",
            },
            ["ODI"] = () => new CricketMatchRules
            {
                OversPerInnings = 50,
                MaxOversPerBowler = 10,
                PowerplayOvers = "1-10,11-40,41-50",
            },
            ["The Hundred"] = () => new CricketMatchRules
            {
                OversPerInnings = 20,
                BallsPerOver = 5,
                MaxOversPerBowler = 4,
                PowerplayOvers = "1-5",
            },
            ["Test"] = () => new CricketMatchRules
            {
                Format = CricketFormat.MultiInningsTimed,
                InningsPerSide = 2,
                OversPerInnings = null,
                MaxOversPerBowler = null,
                DrawAllowed = true,
                FollowOnMargin = 200,
                TieResolution = TieResolution.AllowTie,
            },
            // Short-format two-innings cricket, played in some local leagues.
            ["Two-innings limited overs"] = () => new CricketMatchRules
            {
                Format = CricketFormat.MultiInningsLimitedOvers,
                InningsPerSide = 2,
                OversPerInnings = 10,
                MaxOversPerBowler = 3,
                TieResolution = TieResolution.AllowTie,
            },
            ["Box / Gully"] = () => new CricketMatchRules
            {
                OversPerInnings = 6,
                MaxOversPerBowler = 2,
                PlayersPerSide = 6,
                LastManStanding = true,
                BallType = BallType.Tennis,
                PitchType = PitchType.Concrete,
                LbwEnabled = false,
            },
        };

    public static CricketMatchRules? Create(string? presetName) =>
        presetName is not null && All.TryGetValue(presetName, out var factory) ? factory() : null;
}
