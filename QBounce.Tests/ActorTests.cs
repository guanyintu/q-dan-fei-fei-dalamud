using System.Numerics;
using Xunit;

namespace QBounce.Tests;

public sealed class ActorTests
{
    private static readonly BounceSettings All = new(.4f, .45f, false, true, true, true, true);
    private static void NoHook(ActorSnapshot _) { }
    internal sealed class Access : IActorAccess
    {
        public int Capacity => 32;
        public readonly Dictionary<int, ActorSnapshot> Actors = [];
        public readonly Dictionary<int, Vector3> Scales = [];
        public int Writes;
        public bool RejectWrites;
        public ActorSnapshot Add(int slot, uint entity = 1, ActorGroup group = ActorGroup.Npc, nint draw = 500)
        {
            var actor = new ActorSnapshot(new(slot, 100 + slot, entity, 20, 30, 40, 3, 200, draw, 600, 700, 800), group, entity, 900);
            Actors[slot] = actor;
            Scales[slot] = new Vector3(1.2f, 1.5f, .9f);
            return actor;
        }
        public bool TryGet(int index, out ActorSnapshot snapshot) => Actors.TryGetValue(index, out snapshot);
        public bool TryReadScale(in ActorSnapshot snapshot, out Vector3 scale)
        {
            scale = default;
            return TryGet(snapshot.Identity.Index, out var now) && now.Identity == snapshot.Identity && Scales.TryGetValue(snapshot.Identity.Index, out scale);
        }
        public bool TryWriteScale(in ActorSnapshot snapshot, Vector3 scale)
        {
            if (RejectWrites || !TryReadScale(snapshot, out _)) return false;
            Writes++; Scales[snapshot.Identity.Index] = scale; return true;
        }
    }

    [Fact]
    public void ThousandsOfFramesAndMultipleNativeUpdatesDoNotAccumulate()
    {
        var a = new Access(); a.Add(0); var original = a.Scales[0]; var manager = new ActorManager(a);
        for (var i = 0; i < 10000; i++)
        {
            var time = i / 60d;
            manager.BeginFrame(time, All, NoHook);
            manager.AfterNativeUpdate(0, 100);
            manager.AfterNativeUpdate(0, 100);
            Assert.Equal(original * BounceEngine.Evaluate(time, .4f, .45f), a.Scales[0]);
            Assert.Equal(1, manager.ProcessedCount);
        }
        manager.RestoreAndClear(); Assert.Equal(original, a.Scales[0]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SynchronizedOrRandomStableAcrossActors(bool random)
    {
        var a = new Access(); a.Add(0, 123); a.Add(1, 456); var original = a.Scales[0]; var manager = new ActorManager(a);
        manager.BeginFrame(.12, All with { RandomPhase = random }, NoHook);
        manager.AfterNativeUpdate(0, 100); manager.AfterNativeUpdate(1, 101);
        if (random) Assert.NotEqual(a.Scales[0], a.Scales[1]); else Assert.Equal(a.Scales[0], a.Scales[1]);
        Assert.Equal(original * BounceEngine.Evaluate(.12, .4f, .45f, random ? BounceEngine.Phase(123) : 0), a.Scales[0]);
    }

    [Fact]
    public void NewNpcDiscoveredWithinEightFrames()
    {
        var a = new Access(); var manager = new ActorManager(a); manager.BeginFrame(0, All, NoHook);
        a.Add(31);
        for (var i = 0; i < 8; i++) manager.BeginFrame(.1, All, NoHook);
        manager.AfterNativeUpdate(31, 131); Assert.Equal(1, manager.ProcessedCount);
    }

    [Fact]
    public void DisappearedActorNeverWrittenOnRestore()
    {
        var a = new Access(); a.Add(0); var manager = new ActorManager(a);
        manager.BeginFrame(.1, All, NoHook); manager.AfterNativeUpdate(0, 100);
        a.Actors.Remove(0); var writes = a.Writes;
        manager.BeginFrame(.2, All, NoHook); manager.RestoreAndClear();
        Assert.Equal(writes, a.Writes); Assert.Equal(0, manager.TrackedCount);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void SameAddressWithNewIdentityOrRedrawnModelNeverReceivesOldRestore(bool entityChanged)
    {
        var a = new Access(); a.Add(0); var manager = new ActorManager(a);
        manager.BeginFrame(.1, All, NoHook); manager.AfterNativeUpdate(0, 100);
        a.Add(0, entityChanged ? 2u : 1u, draw: entityChanged ? 500 : 501);
        var clean = a.Scales[0]; var writes = a.Writes;
        manager.RestoreAndClear(); Assert.Equal(writes, a.Writes); Assert.Equal(clean, a.Scales[0]);
    }

    [Fact]
    public void SwitchAreaAndReenableStartsFresh()
    {
        var a = new Access(); a.Add(0); var clean = a.Scales[0]; var manager = new ActorManager(a);
        manager.BeginFrame(.1, All, NoHook); manager.AfterNativeUpdate(0, 100); manager.RestoreAndClear();
        Assert.Equal(clean, a.Scales[0]); a.Actors.Clear(); a.Add(0, 55);
        manager.BeginFrame(.1, All, NoHook); manager.AfterNativeUpdate(0, 100); Assert.Equal(1, manager.ProcessedCount);
        manager.RestoreAndClear(); manager.RestoreAndClear(); Assert.Equal(clean, a.Scales[0]);
    }

    [Fact]
    public void ExternalNewScaleIsPreservedAndBecomesNewBaseline()
    {
        var a = new Access(); a.Add(0); var manager = new ActorManager(a);
        manager.BeginFrame(.1, All, NoHook); manager.AfterNativeUpdate(0, 100);
        var external = new Vector3(1.1f, 1.7f, 1.3f); a.Scales[0] = external;
        manager.BeginFrame(.2, All, NoHook); Assert.Equal(external, a.Scales[0]);
        manager.AfterNativeUpdate(0, 100); manager.RestoreAndClear(); Assert.Equal(external, a.Scales[0]);
    }

    [Fact]
    public void PartialExternalWritePreservesThatAxisWithoutAccumulatingOtherAxes()
    {
        var a = new Access(); a.Add(0); var original = a.Scales[0]; var manager = new ActorManager(a);
        manager.BeginFrame(.1, All, NoHook); manager.AfterNativeUpdate(0, 100);
        a.Scales[0] = a.Scales[0] with { Y = 2.5f };
        manager.BeginFrame(.2, All, NoHook);
        Assert.Equal(new Vector3(original.X, 2.5f, original.Z), a.Scales[0]);
        manager.AfterNativeUpdate(0, 100); manager.RestoreAndClear();
        Assert.Equal(new Vector3(original.X, 2.5f, original.Z), a.Scales[0]);
    }

    [Fact]
    public void GameOverwritingScaleAfterFrameworkIsHandledByNativeCallback()
    {
        var a = new Access(); a.Add(0); var manager = new ActorManager(a);
        manager.BeginFrame(.1, All, NoHook); a.Scales[0] = Vector3.One;
        manager.AfterNativeUpdate(0, 100); Assert.Equal(BounceEngine.Evaluate(.1, .4f, .45f), a.Scales[0]);
    }

    [Fact]
    public void ExcludedGroupRestoredImmediately()
    {
        var a = new Access(); a.Add(0); var original = a.Scales[0]; var manager = new ActorManager(a);
        manager.BeginFrame(.1, All, NoHook); manager.AfterNativeUpdate(0, 100);
        manager.BeginFrame(.2, All with { Npcs = false }, NoHook);
        Assert.Equal(original, a.Scales[0]); Assert.Equal(0, manager.TrackedCount);
    }

    [Fact]
    public void InvalidScaleAddressAndFailedWritesAreIgnored()
    {
        var a = new Access(); a.Add(0); var manager = new ActorManager(a); manager.BeginFrame(.1, All, NoHook);
        manager.AfterNativeUpdate(-1, 0); manager.AfterNativeUpdate(5000, 0); manager.AfterNativeUpdate(0, 999);
        a.Scales[0] = new Vector3(float.NaN); manager.AfterNativeUpdate(0, 100); Assert.Equal(0, a.Writes);
        a.Scales[0] = Vector3.One; a.RejectWrites = true; manager.AfterNativeUpdate(0, 100);
        Assert.Equal(0, manager.ProcessedCount); manager.RestoreAndClear(); Assert.Equal(0, a.Writes);
    }
}
