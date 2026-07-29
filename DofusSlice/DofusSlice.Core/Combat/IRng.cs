namespace DofusSlice.Core.Combat;

/// <summary>Injectable randomness so fights can be replayed deterministically in tests/sims.</summary>
public interface IRng
{
    /// <summary>Uniform integer in [minInclusive, maxInclusive].</summary>
    int Roll(int minInclusive, int maxInclusive);
}

public sealed class SystemRng : IRng
{
    private readonly Random _random;
    public SystemRng(int? seed = null) => _random = seed is int s ? new Random(s) : new Random();
    public int Roll(int minInclusive, int maxInclusive) =>
        maxInclusive <= minInclusive ? minInclusive : _random.Next(minInclusive, maxInclusive + 1);
}
