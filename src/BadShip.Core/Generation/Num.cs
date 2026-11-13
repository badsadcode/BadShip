namespace BadShip.Generation;

internal static class Num
{
    /// <summary>Like Math.Clamp, but a range that collapsed through rounding (min slightly above max) gives its middle instead of throwing.</summary>
    public static float Clamp(float v, float min, float max) => min > max ? (min + max) * 0.5f : MathF.Min(MathF.Max(v, min), max);

    public static int Clamp(int v, int min, int max) => min > max ? (min + max) / 2 : Math.Min(Math.Max(v, min), max);
}
