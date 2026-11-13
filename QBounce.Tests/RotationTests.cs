using System.Numerics;
using Xunit;

namespace QBounce.Tests;

public sealed class RotationTests
{
    [Fact]
    public void SpeedDirectionPauseAndSpeedChangesPreserveContinuousPhase()
    {
        var clock = new RotationClock(); clock.Advance(0,90,true);
        var quarter = clock.Advance(1,90,true);
        Assert.InRange(Math.Abs(Quaternion.Dot(quarter, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI/2))), .99999f, 1.00001f);
        clock.Advance(2,0,true); Assert.Equal(90d, clock.Degrees);
        clock.Advance(2.5,180,true); Assert.Equal(180d, clock.Degrees);
        clock.Advance(3.5,-90,true); Assert.Equal(90d, clock.Degrees);
        clock.Advance(10,90,false); Assert.Equal(0d, clock.Degrees);
        Assert.Equal(Quaternion.Identity, clock.Advance(11,90,true));
    }

    [Fact]
    public void LongClockAndInvalidInputsNeverProduceInvalidQuaternion()
    {
        var clock = new RotationClock(); clock.Advance(0,720,true);
        Assert.True(OwnedRotation.Valid(clock.Advance(315360000.125,720,true)));
        Assert.True(OwnedRotation.Valid(clock.Advance(315360001,float.NaN,true)));
        Assert.Equal(Quaternion.Identity, clock.Advance(double.NaN,90,true));
        Assert.False(OwnedRotation.Valid(default));
        var config = new Configuration { RotationSpeed = float.NaN }; config.Normalize();
        Assert.False(config.Rotate); Assert.Equal(90f, config.RotationSpeed);
    }

    [Fact]
    public void ManyFramesAndRepeatedCallbacksDoNotAccumulateAndRestoreExactOriginal()
    {
        var access = new ActorTests.Access(); access.Add(0); var original = access.Rotations[0];
        var manager = new ActorManager(access);
        var settings = new BounceSettings(.4f,.45f,false,true,true,true,true,true,90);
        for (var i=0; i<1000; i++) {
            manager.BeginFrame(i/60d,settings,_=>{});
            manager.AfterNativeUpdate(0,100); var first = access.Rotations[0];
            manager.AfterNativeUpdate(0,100); Assert.Equal(first, access.Rotations[0]);
            Assert.True(OwnedRotation.Valid(first));
        }
        manager.RestoreAndClear(); Assert.Equal(original, access.Rotations[0]);
    }

    [Fact]
    public void ToggleOffRestoresWhileExternalFacingAndAddressReuseArePreserved()
    {
        var access = new ActorTests.Access(); access.Add(0); var original = access.Rotations[0];
        var manager = new ActorManager(access); var settings = new BounceSettings(0,.45f,false,true,true,true,true,true,90);
        manager.BeginFrame(0,settings,_=>{}); manager.AfterNativeUpdate(0,100);
        manager.BeginFrame(1,settings,_=>{}); manager.AfterNativeUpdate(0,100);
        Assert.NotEqual(original, access.Rotations[0]);
        manager.BeginFrame(2,settings with { Rotate=false },_=>{}); Assert.Equal(original, access.Rotations[0]);
        manager.AfterNativeUpdate(0,100); Assert.Equal(original, access.Rotations[0]);
        manager.BeginFrame(3,settings,_=>{}); manager.AfterNativeUpdate(0,100);
        var external = Quaternion.CreateFromYawPitchRoll(2,.4f,.1f); access.Rotations[0] = external;
        manager.RestoreAndClear(); Assert.Equal(external, access.Rotations[0]);
        manager.BeginFrame(4,settings,_=>{}); manager.AfterNativeUpdate(0,100);
        access.Add(0,99); var replacement = access.Rotations[0]; manager.RestoreAndClear();
        Assert.Equal(replacement, access.Rotations[0]);
    }
}
