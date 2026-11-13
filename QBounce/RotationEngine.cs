using System.Numerics;

namespace QBounce;

public sealed class RotationClock
{
    private double previous;
    private bool active;
    public double Degrees { get; private set; }
    public Quaternion Advance(double seconds, float speed, bool enabled)
    {
        if (!enabled || !double.IsFinite(seconds)) { Reset(); return Quaternion.Identity; }
        if (!active || seconds < previous) { Degrees = 0; previous = seconds; active = true; }
        speed = float.IsFinite(speed) ? Math.Clamp(speed, -720, 720) : 0;
        if (speed != 0)
        {
            Degrees = (Degrees + (seconds - previous) % (360d / Math.Abs(speed)) * speed) % 360;
            if (Degrees < 0) Degrees += 360;
        }
        previous = seconds;
        return Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(Degrees * Math.PI / 180));
    }
    public void Reset() { active = false; previous = Degrees = 0; }
}

public struct OwnedRotation
{
    private Quaternion baseline, last;
    public bool Owned;
    public static bool Valid(Quaternion value) => float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W) && value.LengthSquared() is > .5f and < 1.5f;
    public Quaternion WithoutOwn(Quaternion current) => Owned && (current == last || current == -last) ? baseline : current;
    public Quaternion Apply(Quaternion current, Quaternion relative)
    {
        baseline = WithoutOwn(current);
        last = Quaternion.Normalize(relative * Quaternion.Normalize(baseline));
        Owned = true;
        return last;
    }
}
