namespace DofusSlice.Core.Combat;

/// <summary>
/// How a unit is played by the autobattler. TITHE combat is <b>watched, not piloted</b>: every
/// unit — including the player's crew — acts through one of these policies. The policy is the
/// thing the player reads during a fight ("the archer kept range, the bulwark held the line").
/// </summary>
public enum AiPolicy
{
    /// <summary>Advance, body-block, hit the nearest enemy. Front-line bruisers (Bulwark, Barrow Husk).</summary>
    Bruiser,

    /// <summary>Fast melee that dives the softest reachable target. Flankers (Gravehound).</summary>
    Flanker,

    /// <summary>Keep to a preferred range band, kite away when crowded, shoot the softest target in range.
    /// Long-range skirmishers (Archer, Marrow Spitter).</summary>
    Skirmisher,

    /// <summary>Hold a safe mid-range sightline and nuke the priority (softest) target; retreat if meleed.
    /// Artillery (Cannon).</summary>
    Artillery,
}
