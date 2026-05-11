namespace AgentSim.Core.Rng;

/// <summary>
/// xorshift64* deterministic PRNG. Single seeded instance, save/load preserves state for replay determinism.
/// </summary>
public sealed class Prng
{
    private ulong _state;

    public Prng(ulong seed)
    {
        // xorshift requires a non-zero seed.
        _state = seed == 0 ? 0xBADBAD1234567890UL : seed;
    }

    public ulong NextUInt64()
    {
        ulong x = _state;
        x ^= x << 13;
        x ^= x >> 7;
        x ^= x << 17;
        _state = x;
        return x * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>Uniform integer in [0, exclusiveMax).</summary>
    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        return (int)(NextUInt64() % (ulong)exclusiveMax);
    }

    /// <summary>Uniform double in [0.0, 1.0).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Current internal state, for save/load.</summary>
    public ulong State
    {
        get => _state;
        set => _state = value == 0 ? 0xBADBAD1234567890UL : value;
    }
}
