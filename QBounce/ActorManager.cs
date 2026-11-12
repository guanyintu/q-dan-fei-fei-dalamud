using System.Numerics;

namespace QBounce;

public enum ActorGroup { Self, Player, Npc, Monster }

// An address alone is never an actor identity. Include the model/skeleton and native vtables.
public readonly record struct ActorIdentity(int Index, nint Address, uint EntityId, uint BaseId,
    uint LayoutId, uint OwnerId, byte Kind, nint VTable, nint Draw, nint DrawVTable, nint Skeleton, int ModelId);
public readonly record struct ActorSnapshot(ActorIdentity Identity, ActorGroup Group, ulong PhaseKey, nint UpdateFunction);

public interface IActorAccess
{
    int Capacity { get; }
    bool TryGet(int index, out ActorSnapshot snapshot);
    bool TryReadScale(in ActorSnapshot snapshot, out Vector3 scale);
    bool TryWriteScale(in ActorSnapshot snapshot, Vector3 scale);
}

public readonly record struct BounceSettings(float Amplitude, float Period, bool RandomPhase,
    bool Self, bool Players, bool Npcs, bool Monsters)
{
    public bool Includes(ActorGroup group) => group switch
    {
        ActorGroup.Self => Self, ActorGroup.Player => Players, ActorGroup.Npc => Npcs,
        ActorGroup.Monster => Monsters, _ => false,
    };
}

public sealed class ActorManager(IActorAccess access)
{
    private sealed class Entry(ActorSnapshot actor)
    {
        public readonly ActorSnapshot Actor = actor;
        public Vector3 Baseline;
        public Vector3 LastWritten;
        public bool OwnsScale;
        public long AppliedFrame = -1;
    }

    private readonly Entry?[] entries = new Entry?[access.Capacity];
    private int scanCursor;
    private long frame;
    private double seconds;
    private BounceSettings settings;
    public int TrackedCount { get; private set; }
    public int ProcessedCount { get; private set; }

    public void BeginFrame(double elapsed, BounceSettings options, Action<ActorSnapshot> ensureHook)
    {
        seconds = elapsed;
        settings = options;
        ProcessedCount = 0;
        frame++;
        // Active entries are revalidated every frame. Empty slots are discovered in a rolling scan.
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry == null) continue;
            if (!access.TryGet(i, out var current) || current.Identity != entry.Actor.Identity)
            {
                // Never restore through the old pointer after a despawn/redraw/reuse.
                entries[i] = null;
                TrackedCount--;
                continue;
            }
            Restore(entry);
            if (!settings.Includes(current.Group))
            {
                entries[i] = null;
                TrackedCount--;
            }
        }
        // A complete discovery pass within eight frames; enable starts with a full pass.
        var budget = frame == 1 ? entries.Length : Math.Max(1, (entries.Length + 7) / 8);
        for (var n = 0; n < budget; n++)
        {
            var i = scanCursor++ % entries.Length;
            if (scanCursor >= entries.Length) scanCursor = 0;
            if (entries[i] != null || !access.TryGet(i, out var actor) || !settings.Includes(actor.Group)) continue;
            ensureHook(actor);
            entries[i] = new Entry(actor);
            TrackedCount++;
        }
    }

    // Called after the native actor update, on the Framework thread only.
    public void AfterNativeUpdate(int index, nint address)
    {
        if ((uint)index >= (uint)entries.Length || entries[index] is not { } entry ||
            entry.Actor.Identity.Address != address || !access.TryReadScale(entry.Actor, out var current)) return;
        if (entry.OwnsScale) current = RemoveOwnedComponents(current, entry);
        entry.OwnsScale = false;
        if (!BounceEngine.ValidScale(current)) return;
        var phase = settings.RandomPhase ? BounceEngine.Phase(entry.Actor.PhaseKey) : 0;
        var scale = current * BounceEngine.Evaluate(seconds, settings.Amplitude, settings.Period, phase);
        if (!BounceEngine.ValidScale(scale)) return;
        entry.Baseline = current;
        entry.LastWritten = scale;
        // Mark ownership first so recovery can undo a write even if transform notification throws.
        entry.OwnsScale = true;
        if (!access.TryWriteScale(entry.Actor, scale)) entry.OwnsScale = false;
        else if (entry.AppliedFrame != frame)
        {
            entry.AppliedFrame = frame;
            ProcessedCount++;
        }
    }

    private void Restore(Entry entry)
    {
        if (!entry.OwnsScale) return;
        // Compare-and-restore: an external writer's new scale takes precedence.
        if (access.TryReadScale(entry.Actor, out var current))
        {
            var restored = RemoveOwnedComponents(current, entry);
            if (current != restored) access.TryWriteScale(entry.Actor, restored);
        }
        entry.OwnsScale = false;
    }

    private static Vector3 RemoveOwnedComponents(Vector3 current, Entry entry) => new(
        current.X == entry.LastWritten.X ? entry.Baseline.X : current.X,
        current.Y == entry.LastWritten.Y ? entry.Baseline.Y : current.Y,
        current.Z == entry.LastWritten.Z ? entry.Baseline.Z : current.Z);

    public void RestoreAndClear()
    {
        List<Exception>? errors = null;
        for (var i = 0; i < entries.Length; i++)
        {
            if (entries[i] is not { } entry) continue;
            try { Restore(entry); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
            entries[i] = null;
        }
        TrackedCount = ProcessedCount = scanCursor = 0;
        frame = 0;
        if (errors != null) throw new AggregateException("部分模型无法恢复，请切图或重新登录。", errors);
    }
}
