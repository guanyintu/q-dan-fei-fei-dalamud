using System.Numerics;
using Xunit;

namespace QBounce.Tests;

public sealed class BounceTests
{
    [Fact]
    public void DefaultRangeAndVolumeCompensation()
    {
        for (var i = 0; i <= 10000; i++)
        {
            var scale = BounceEngine.Evaluate(i * .45f / 10000, .4f, .45f);
            Assert.InRange(scale.Y, .6f - 1e-6f, 1.4f + 1e-6f);
            Assert.InRange(Math.Abs(scale.X - 1 / MathF.Sqrt(scale.Y)), 0, 1e-6f);
            Assert.Equal(scale.X, scale.Z);
            Assert.InRange(Math.Abs(scale.X * scale.Y * scale.Z - 1), 0, 3e-7f);
        }
        Assert.InRange(Math.Abs(BounceEngine.Evaluate(.45f / 4, .4f, .45f).Y - 1.4f), 0, 1e-6f);
        Assert.InRange(Math.Abs(BounceEngine.Evaluate(.45f * .75, .4f, .45f).Y - .6f), 0, 1e-6f);
    }

    [Theory]
    [InlineData(0)] [InlineData(.2)] [InlineData(12345678)] [InlineData(315360000)]
    public void MonotonicTimeIsPeriodicEvenAfterYears(double time)
    {
        var a = BounceEngine.Evaluate(time, .4f, .45f);
        var b = BounceEngine.Evaluate(time + .45f, .4f, .45f);
        Assert.InRange(Vector3.Distance(a, b), 0, 1e-6);
    }

    [Fact]
    public void PhaseIsDeterministicAndDistributed()
    {
        Assert.Equal(BounceEngine.Phase(123), BounceEngine.Phase(123));
        Assert.NotEqual(BounceEngine.Phase(123), BounceEngine.Phase(124));
        for (ulong i = 0; i < 1000; i++) Assert.InRange(BounceEngine.Phase(i), 0, Math.BitDecrement(1d));
    }

    [Theory]
    [InlineData(float.NaN, .45f)] [InlineData(float.PositiveInfinity, .45f)]
    [InlineData(.4f, float.NaN)] [InlineData(.6f, 0)] [InlineData(-1, -1)] [InlineData(999, 999)]
    public void InvalidSettingsProduceFiniteSafeScales(float amplitude, float period)
        => Assert.True(BounceEngine.ValidScale(BounceEngine.Evaluate(1, amplitude, period)));

    [Fact]
    public void InvalidTimeAndZeroAmplitudeAreIdentity()
    {
        Assert.Equal(Vector3.One, BounceEngine.Evaluate(double.NaN, .4f, .45f));
        Assert.Equal(Vector3.One, BounceEngine.Evaluate(10, 0, .45f));
    }
}
