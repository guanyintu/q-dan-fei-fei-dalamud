using System.Numerics;

namespace QBounce;

public static class BounceEngine
{
    public static Vector3 Evaluate(double seconds, float amplitude, float period, double phase = 0)
    {
        if (!double.IsFinite(seconds) || !double.IsFinite(phase)) return Vector3.One;
        amplitude = float.IsFinite(amplitude) ? Math.Clamp(amplitude, 0, .6f) : 0;
        period = float.IsFinite(period) ? Math.Clamp(period, .2f, 1.5f) : .45f;
        // Reduce in double precision before evaluating; never accumulate float delta times.
        var cycles = seconds / period + phase;
        var vertical = 1f + amplitude * (float)Math.Sin(Math.Tau * (cycles - Math.Floor(cycles)));
        var horizontal = 1f / MathF.Sqrt(vertical);
        return new Vector3(horizontal, vertical, horizontal);
    }

    public static double Phase(ulong identity)
    {
        // SplitMix64: stable across frames and process restarts, independent of enumeration order.
        var value = identity + 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (value >> 11) * (1.0 / (1UL << 53));
    }

    public static bool ValidScale(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) &&
        value.X is > .0001f and < 10000 && value.Y is > .0001f and < 10000 && value.Z is > .0001f and < 10000;
}
