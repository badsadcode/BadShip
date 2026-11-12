namespace BadShip.Generation;

/// <summary>
/// xoshiro256** seeded through SplitMix64. Written out here (instead of System.Random, whose algorithm .NET does not
/// promise to keep) so a seed gives the same ship on every platform and future runtime, and so a port can match it.
/// </summary>
public sealed class Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public Rng(ulong seed)
    {
        var x = seed;
        _s0 = SplitMix(ref x);
        _s1 = SplitMix(ref x);
        _s2 = SplitMix(ref x);
        _s3 = SplitMix(ref x);
    }

    /// <summary>An independent stream for one generation stage, so stages don't reshuffle each other.</summary>
    public static Rng Stream(ulong seed, string stage) => new(seed ^ Fnv1a(stage));

    public ulong NextU64()
    {
        var result = Rotl(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextU64() >> 11) * (1.0 / (1UL << 53));

    public float Range(float min, float max) => min + (float)NextDouble() * (max - min);

    /// <summary>Uniform integer in [min, max] inclusive.</summary>
    public int Int(int min, int max) => max <= min ? min : min + (int)(NextU64() % (ulong)(max - min + 1));

    public bool Chance(double p) => NextDouble() < p;

    public float Sign() => Chance(0.5) ? 1f : -1f;

    public T Pick<T>(IReadOnlyList<T> items) => items[Int(0, items.Count - 1)];

    public T Weighted<T>(IReadOnlyList<(T item, double weight)> items)
    {
        var total = items.Sum(i => i.weight);
        var x = NextDouble() * total;
        foreach (var (item, w) in items)
        {
            if (x < w) return item;
            x -= w;
        }
        return items[^1].item;
    }

    public static ulong Fnv1a(string s)
    {
        var h = 14695981039346656037UL;
        foreach (var ch in s)
        {
            h ^= ch;
            h *= 1099511628211UL;
        }
        return h;
    }

    private static ulong SplitMix(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        var z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));
}
